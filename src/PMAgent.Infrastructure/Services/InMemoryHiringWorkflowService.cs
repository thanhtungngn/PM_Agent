using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Collections.Concurrent;
using System.Text;

namespace PMAgent.Infrastructure.Services;

public sealed class InMemoryHiringWorkflowService : IHiringWorkflowService
{
    private const string AwaitingScreeningApprovalStage = "awaiting_screening_approval";
    private const string AwaitingInterviewApprovalStage = "awaiting_interview_approval";
    private const string InterviewActiveStage = "interview_active";
    private const string CompletedStage = "completed";
    private const string RejectedStage = "rejected";

    private static readonly string[] SupportedTechnicalRoles = ["DEV"];
    private static readonly ConcurrentDictionary<Guid, HiringSessionState> Sessions = new();

    private readonly IHiringFitScoringAgent _fitScoringAgent;
    private readonly IInterviewScoringAgent _scoringAgent;
    private readonly IInterviewQuestionProvider _questionProvider;
    private readonly HiringWorkflowSettings _settings;
    private readonly ILogger<InMemoryHiringWorkflowService> _logger;
    private readonly string _notesRootPath;

    public InMemoryHiringWorkflowService(
        IHiringFitScoringAgent fitScoringAgent,
        IInterviewScoringAgent scoringAgent,
        IInterviewQuestionProvider questionProvider,
        HiringWorkflowSettings settings,
        ILogger<InMemoryHiringWorkflowService> logger,
        string? notesRootPath = null)
    {
        _fitScoringAgent = fitScoringAgent;
        _scoringAgent = scoringAgent;
        _questionProvider = questionProvider;
        _settings = settings;
        _logger = logger;
        _notesRootPath = notesRootPath ?? ResolveNotesRootPath();
        Directory.CreateDirectory(_notesRootPath);
    }

    public Task<HiringSessionResult?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Sessions.TryGetValue(sessionId, out var state) ? ToResult(state) : null);
    }

    public async Task<HiringSessionResult> StartAsync(HiringSessionStartRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var technicalRole = NormalizeTechnicalRole(request.TechnicalInterviewRole);
        var state = new HiringSessionState(request, technicalRole);
        state.SeniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
        state.ConversationLanguage = HiringConversationLanguageResolver.ResolveInitialLanguage(request.ProjectBrief, request.JobDescription, request.CandidateCv, request.Context);
        var fitAssessment = await _fitScoringAgent.EvaluateAsync(
            request.ProjectBrief,
            request.JobDescription,
            request.CandidateCv,
            state.SeniorityLevel,
            technicalRole,
            cancellationToken);

        state.ScreeningFitScore = fitAssessment.Score;
        state.ScreeningSummary = fitAssessment.Summary;
        state.ScreeningStrengths.AddRange(fitAssessment.Strengths);
        state.ScreeningGaps.AddRange(fitAssessment.Gaps);
        state.Participants.Add("HR");

        // Create per-candidate folder and write keyword files for reuse during the interview
        CreateCandidateFolder(state);

        var screeningSummary = BuildScreeningSummary(state);
        AddTurn(state, "HR", screeningSummary);

        if (!fitAssessment.ShouldAdvance || state.ScreeningFitScore < _settings.ScreeningPassThreshold)
        {
            state.Stage = RejectedStage;
            state.StatusSummary = "HR screening rejected the candidate because fit is below the threshold.";
            state.CurrentSpeaker = "HR";
            state.CurrentPrompt = $"The screening fit is below {_settings.ScreeningPassThreshold:F0}%, so the process stops here.";
            WriteNotesFile(state);
            return ToResult(state);
        }

        state.Stage = AwaitingScreeningApprovalStage;
        state.RequiresUserApproval = true;
        state.ApprovalType = "screening_forward";
        state.CurrentSpeaker = "HR";
        state.CurrentPrompt = $"The CV fit score is {state.ScreeningFitScore:F1}%. Do you approve forwarding this CV to PM and {technicalRole}?";
        state.StatusSummary = "HR screening passed. Waiting for user approval to forward the CV.";

        Sessions[state.SessionId] = state;
        _logger.LogInformation("[HiringWorkflow] Started session {SessionId} with screening fit {FitScore}", state.SessionId, state.ScreeningFitScore);
        return ToResult(state);
    }

    public async Task<HiringSessionResult> ApproveScreeningAsync(Guid sessionId, HiringApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = GetRequiredSession(sessionId);

        if (!string.Equals(state.Stage, AwaitingScreeningApprovalStage, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Session is not waiting for screening approval.");

        AddTurn(state, "USER", request.Approved ? $"Approved forwarding CV. {request.Comment}".Trim() : $"Rejected forwarding CV. {request.Comment}".Trim());

        if (!request.Approved)
        {
            state.Stage = RejectedStage;
            state.RequiresUserApproval = false;
            state.ApprovalType = string.Empty;
            state.CurrentSpeaker = "HR";
            state.CurrentPrompt = "The hiring workflow stops because the user did not approve forwarding the CV.";
            state.StatusSummary = "User stopped the process after HR screening.";
            WriteNotesFile(state);
            return ToResult(state);
        }

        state.Participants.Clear();
        state.Participants.AddRange(["HR", "PM", state.TechnicalInterviewRole]);

        var schedulingMessage = BuildSchedulingMessage(state);
        AddTurn(state, "PM", schedulingMessage);

        if (state.Request.AutoApproveInterviewSchedule)
        {
            AddTurn(state, "USER", "Interview schedule approved automatically by default.");
            await BeginInterviewAsync(state, cancellationToken);
            state.StatusSummary = "Interview schedule auto-approved. Interview started immediately.";
            return ToResult(state);
        }

        state.Stage = AwaitingInterviewApprovalStage;
        state.RequiresUserApproval = true;
        state.ApprovalType = "interview_schedule";
        state.CurrentSpeaker = "PM";
        state.CurrentPrompt = "PM has prepared the interview schedule. Do you approve starting the interview?";
        state.StatusSummary = "Waiting for user approval to start the interview.";
        return ToResult(state);
    }

    public async Task<HiringSessionResult> ApproveInterviewScheduleAsync(Guid sessionId, HiringApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = GetRequiredSession(sessionId);

        if (!string.Equals(state.Stage, AwaitingInterviewApprovalStage, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Session is not waiting for interview schedule approval.");

        AddTurn(state, "USER", request.Approved ? $"Approved interview schedule. {request.Comment}".Trim() : $"Rejected interview schedule. {request.Comment}".Trim());

        if (!request.Approved)
        {
            state.Stage = CompletedStage;
            state.RequiresUserApproval = false;
            state.ApprovalType = string.Empty;
            state.CurrentSpeaker = "PM";
            state.CurrentPrompt = "The interview was not approved to proceed.";
            state.StatusSummary = "Interview schedule was rejected by the user.";
            WriteNotesFile(state);
            return ToResult(state);
        }

        await BeginInterviewAsync(state, cancellationToken);
        state.StatusSummary = "Interview approved. Panel introductions completed and candidate introduction requested.";
        return ToResult(state);
    }

    public async Task<HiringSessionResult> SubmitCandidateResponseAsync(Guid sessionId, HiringCandidateResponseRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = GetRequiredSession(sessionId);

        if (!string.Equals(state.Stage, InterviewActiveStage, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Session is not in an active interview stage.");

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Candidate response cannot be empty.", nameof(request));

        var message = request.Message.Trim();

        if (!state.InterviewLanguageLocked)
            state.ConversationLanguage = HiringConversationLanguageResolver.ResolveUpdatedLanguage(state.ConversationLanguage, message);

        if (state.AwaitingLanguageSelection)
            return await HandleLanguageSelectionAsync(state, message, cancellationToken);

        // If the candidate is requesting a hint through the flag or natural language, redirect to hint logic
        if (request.IsHintRequest || IsHintRequestMessage(message))
            return await RequestHintAsync(sessionId, cancellationToken);

        // Detect non-answer requests from the candidate and keep the session conversational.
        if (IsCandidateInterviewRequest(message))
        {
            return await HandleCandidateInterviewRequestAsync(state, message, cancellationToken);
        }

        // Candidate answered — record it
        state.WaitingForClarificationAnswer = false;
        state.CandidateRequestCountForCurrentQuestion = 0;
        AddTurn(state, "CANDIDATE", message);
        state.CandidateResponseCount++;
        AppendQaEntry(state, "CANDIDATE", message);
        AddTurn(state, "HR", BuildHrInterviewNote(state, message));

        // Evaluate score
        var score = await _scoringAgent.EvaluateAsync(
            state.Request.ProjectBrief,
            state.Request.JobDescription,
            state.SeniorityLevel,
            state.TechnicalInterviewRole,
            state.Transcript,
            state.CandidateResponseCount,
            cancellationToken);

        state.AnswerScores.Add(score.Score);
        state.InterviewScore = state.AnswerScores.Average();
        AddTurn(state, "EVAL", BuildEvaluationSummary(score, state.InterviewScore));
        var interviewerFeedback = BuildInterviewerFeedback(state, score);
        AddTurn(state, state.CurrentSpeaker, interviewerFeedback);
        AppendQaEntry(state, state.CurrentSpeaker, $"[FEEDBACK] {interviewerFeedback}");

        if (score.ShouldStop && state.CandidateResponseCount >= _settings.Scoring.MinimumResponsesBeforeStop)
        {
            CompleteInterview(state, "Interview stopped early because the score dropped below the acceptable threshold.");
            return ToResult(state);
        }

        // Offer follow-up if the active question has one and it has not been asked yet
        if (state.ActiveQuestion?.FollowUpText is { } followUp && !state.FollowUpAsked && score.ShouldAskFollowUp)
        {
            state.FollowUpAsked = true;
            state.HintCountForCurrentQuestion = 0;
            state.CurrentPrompt = followUp;
            AddTurn(state, state.CurrentSpeaker, followUp);
            AppendQaEntry(state, state.CurrentSpeaker, followUp);
            state.StatusSummary = $"Follow-up question from {state.CurrentSpeaker}.";
            return ToResult(state);
        }

        // Move to next question or close
        if (state.PendingQuestions.Count == 0)
        {
            CompleteInterview(state, "Interview objectives have been covered. Moving to Q/A and closing.");
            return ToResult(state);
        }

        await MoveToNextQuestionAsync(state, cancellationToken);
        state.StatusSummary = $"Interview in progress. Current speaker: {state.CurrentSpeaker}.";
        return ToResult(state);
    }

    public Task<HiringSessionResult> RequestHintAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = GetRequiredSession(sessionId);

        if (!string.Equals(state.Stage, InterviewActiveStage, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Session is not in an active interview stage.");

        var hints = state.ActiveQuestion?.HintKeywords ?? [];
        string hintText;

        if (hints.Count > 0)
        {
            var selectedHints = hints.Take(_settings.HintKeywordCount).ToArray();
            hintText = IsVietnamese(state)
                ? $"Mình gợi ý cho bạn vài ý ngắn để suy nghĩ tiếp: {string.Join(", ", selectedHints)}. Bạn cứ trả lời theo cách hiểu của mình nhé."
                : $"Here are a few prompts that might help: {string.Join(", ", selectedHints)}. Take your time and explain how you think about the problem.";
        }
        else
        {
            // Derive hints from the matched JD/CV keywords stored in the candidate folder
            var derivedHints = ExtractKeywords(state.Request.JobDescription).Take(_settings.HintKeywordCount).ToArray();
            if (IsVietnamese(state))
            {
                hintText = derivedHints.Length > 0
                    ? $"Bạn có thể thử bám vào các ý này: {string.Join(", ", derivedHints)}. Hãy liên hệ với kinh nghiệm thực tế của bạn."
                    : "Bạn cứ nghĩ về ví dụ gần nhất trong công việc của mình và liên hệ nó với câu hỏi này.";
            }
            else
            {
                hintText = derivedHints.Length > 0
                    ? $"Consider these cues from the job description: {string.Join(", ", derivedHints)}. Try to connect them to your experience and reasoning."
                    : "Think about the most relevant experience you have and how it applies to this question.";
            }
        }

        state.HintCountForCurrentQuestion++;
        AddTurn(state, state.CurrentSpeaker, hintText);
        AppendQaEntry(state, state.CurrentSpeaker, $"[HINT] {hintText}");
        state.CurrentPrompt = hintText;
        state.StatusSummary = IsVietnamese(state)
            ? $"Đã cung cấp gợi ý. Số lần gợi ý cho câu hỏi hiện tại: {state.HintCountForCurrentQuestion}."
            : $"Hint provided. Hint count for this question: {state.HintCountForCurrentQuestion}.";

        return Task.FromResult(ToResult(state));
    }

    private static string NormalizeTechnicalRole(string technicalInterviewRole)
    {
        if (string.IsNullOrWhiteSpace(technicalInterviewRole))
            return "DEV";

        var normalized = technicalInterviewRole.Trim().ToUpperInvariant();
        return SupportedTechnicalRoles.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : "DEV";
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "with", "from", "that", "this", "your", "have", "has", "will", "need",
            "candidate", "project", "role", "years", "team", "experience", "work", "using", "used"
        };

        return text
            .Split([' ', '\n', '\r', '\t', ',', '.', ':', ';', '(', ')', '/', '\\', '-', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => NormalizeKeyword(token.Trim()))
            .Where(token => token.Length >= 3 && !stopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeKeyword(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        var normalized = token.Trim().ToLowerInvariant();

        if (normalized.EndsWith("ies", StringComparison.Ordinal))
            normalized = $"{normalized[..^3]}y";
        else if (normalized.EndsWith("es", StringComparison.Ordinal) && normalized.Length > 4)
            normalized = normalized[..^2];
        else if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 3)
            normalized = normalized[..^1];

        if (normalized.EndsWith("ing", StringComparison.Ordinal) && normalized.Length > 5)
            normalized = normalized[..^3];
        else if (normalized.EndsWith("ed", StringComparison.Ordinal) && normalized.Length > 4)
            normalized = normalized[..^2];

        return normalized switch
        {
            "rest" => string.Empty,
            "automation" => "automat",
            "automat" => "automat",
            "design" => "design",
            "strateg" => "strategy",
            "strategy" => "strategy",
            "plann" => "strategy",
            "plan" => "strategy",
            _ => normalized
        };
    }

    private static string BuildScreeningSummary(HiringSessionState state)
    {
        var strengths = state.ScreeningStrengths.Count > 0
            ? string.Join(", ", state.ScreeningStrengths)
            : "No strong evidence captured.";

        var gaps = state.ScreeningGaps.Count > 0
            ? string.Join(", ", state.ScreeningGaps)
            : "No major gaps identified.";

        return $"""
            HR screening summary

            - Fit score: {state.ScreeningFitScore:F1}%
            - Seniority target: {state.SeniorityLevel}
            - Target technical interviewer: {state.TechnicalInterviewRole}
            - Summary: {state.ScreeningSummary}
            - Strengths: {strengths}
            - Gaps: {gaps}
            """;
    }

    private static string BuildSchedulingMessage(HiringSessionState state)
    {
        return $"""
            PM scheduling summary

            - Panel: PM, HR, {state.TechnicalInterviewRole}
            - Seniority target: {state.SeniorityLevel}
            - Flow: introductions -> language selection -> one-shot question bank generation -> technical interview rounds -> Q/A -> closing
            - Follow-up policy: only ask follow-up questions when the candidate shows solid understanding and provides a good initial answer.
            - Scoring policy: each answer is scored individually, and the session score is the running average of those answer scores.
            - Default behavior: interview can start immediately unless the user wants to pause for schedule approval.
            - Required approval: confirm whether PM should proceed to the interview stage.
            """;
    }

    private async Task BeginInterviewAsync(HiringSessionState state, CancellationToken cancellationToken)
    {
        state.Stage = InterviewActiveStage;
        state.RequiresUserApproval = false;
        state.ApprovalType = string.Empty;

        AddTurn(state, "HR", BuildPanelIntroduction(state, "HR"));
        AddTurn(state, "PM", BuildPanelIntroduction(state, "PM"));
        AddTurn(state, state.TechnicalInterviewRole, BuildPanelIntroduction(state, state.TechnicalInterviewRole));

        state.PendingQuestions.Clear();
        state.AwaitingLanguageSelection = true;
        state.InterviewLanguageLocked = false;
        state.CurrentSpeaker = "HR";
        state.CurrentPrompt = BuildLanguageSelectionPrompt();
        AddTurn(state, "HR", state.CurrentPrompt);
        AppendQaEntry(state, "HR", state.CurrentPrompt);
    }

    private static string BuildEvaluationSummary(InterviewScoreResult score, double aggregateScore)
    {
        var builder = new StringBuilder();
        builder.Append($"Answer score: {score.Score:F1}. Session score: {aggregateScore:F1}. ");
        builder.Append(score.Rationale);

        if (score.Dimensions is { Count: > 0 })
        {
            builder.Append(" Dimensions: ");
            builder.Append(string.Join("; ", score.Dimensions.Select(dimension => $"{dimension.Name}={dimension.Score:F0} ({dimension.Summary})")));
        }

        return builder.ToString();
    }

    private static string BuildInterviewerFeedback(HiringSessionState state, InterviewScoreResult score)
    {
        if (!string.IsNullOrWhiteSpace(score.Feedback))
            return score.Feedback.Trim();

        return IsVietnamese(state)
            ? "Cảm ơn bạn. Ở câu tiếp theo, bạn cứ bám vào một ví dụ thật và nói rõ phần bạn trực tiếp làm nhé."
            : "Thanks. In the next answer, please stay with one real example and be explicit about what you directly handled.";
    }

    private static string BuildHrInterviewNote(HiringSessionState state, string candidateMessage)
    {
        var shortened = candidateMessage.Length <= 180 ? candidateMessage : $"{candidateMessage[..177]}...";
        return $"HR note: candidate response #{state.CandidateResponseCount} - {shortened}";
    }

    private async Task<HiringSessionResult> HandleCandidateInterviewRequestAsync(
        HiringSessionState state,
        string message,
        CancellationToken cancellationToken)
    {
        state.WaitingForClarificationAnswer = true;
        state.PendingClarificationFrom = state.CurrentSpeaker;
        state.CandidateRequestCountForCurrentQuestion++;
        AddTurn(state, "CANDIDATE", message);
        AppendQaEntry(state, "CANDIDATE", message);
        WriteNotesFile(state);

        var interviewerReply = await _questionProvider.BuildInterviewerReplyFromNotesAsync(
            state.Request,
            state.TechnicalInterviewRole,
            state.CurrentSpeaker,
            message,
            state.NotesDocumentPath,
            state.ConversationLanguage,
            cancellationToken);

        if (state.CandidateRequestCountForCurrentQuestion >= _settings.MaxCandidateRequestsPerQuestion)
            interviewerReply = AppendFocusReminder(state, interviewerReply);

        state.CurrentPrompt = interviewerReply;
        AddTurn(state, state.CurrentSpeaker, interviewerReply);
        AppendQaEntry(state, state.CurrentSpeaker, interviewerReply);
        state.WaitingForClarificationAnswer = false;
        state.StatusSummary = BuildClarificationStatusSummary(
            state.ConversationLanguage,
            state.CandidateRequestCountForCurrentQuestion >= _settings.MaxCandidateRequestsPerQuestion);
        return ToResult(state);
    }

    private async Task<HiringSessionResult> HandleLanguageSelectionAsync(
        HiringSessionState state,
        string message,
        CancellationToken cancellationToken)
    {
        AddTurn(state, "CANDIDATE", message);
        AppendQaEntry(state, "CANDIDATE", message);

        state.ConversationLanguage = HiringConversationLanguageResolver.ResolveInterviewLanguageChoice(message, state.ConversationLanguage);
        state.InterviewLanguageLocked = true;
        state.AwaitingLanguageSelection = false;

        var acknowledgement = BuildLanguageSelectionAcknowledgement(state);
        AddTurn(state, "HR", acknowledgement);
        AppendQaEntry(state, "HR", acknowledgement);

        state.PendingQuestions.Clear();
        var questionBank = await _questionProvider.BuildQuestionsAsync(
            state.Request,
            state.TechnicalInterviewRole,
            state.ConversationLanguage,
            cancellationToken);
        foreach (var question in questionBank)
            state.PendingQuestions.Enqueue(question);

        await MoveToNextQuestionAsync(state, cancellationToken);
        state.StatusSummary = string.Equals(state.ConversationLanguage, "VI", StringComparison.OrdinalIgnoreCase)
            ? "Đã chốt ngôn ngữ phỏng vấn và bắt đầu vào câu hỏi chính."
            : "Interview language locked and the main interview questions have started.";
        return ToResult(state);
    }

    private static bool IsCandidateInterviewRequest(string message)
    {
        var lower = message.Trim().ToLowerInvariant();
        return lower.EndsWith('?')
            || lower.StartsWith("before i answer")
            || lower.StartsWith("before answering")
            || lower.StartsWith("can i ask")
            || lower.StartsWith("could i ask")
            || lower.StartsWith("i have a question")
            || lower.StartsWith("one question")
            || lower.StartsWith("quick question")
            || lower.Contains("what do you mean")
            || lower.Contains("could you clarify")
            || lower.Contains("can you explain")
            || lower.Contains("do you mean")
            || lower.Contains("what is meant by")
            || lower.Contains("before i answer")
            || lower.Contains("before answering")
            || lower.Contains("can i ask")
            || lower.Contains("could i ask")
            || lower.Contains("i have a question")
            || lower.Contains("tell me about the team")
            || lower.Contains("interview process")
            || lower.Contains("salary")
            || lower.StartsWith("does that mean")
            || lower.Contains("nghĩa là gì")
            || lower.Contains("làm rõ")
            || lower.Contains("giải thích")
            || lower.Contains("đây là câu hỏi")
            || lower.Contains("day la cau hoi")
            || lower.Contains("trước khi trả lời")
            || lower.Contains("truoc khi tra loi")
            || lower.Contains("cho tôi hỏi")
            || lower.Contains("cho toi hoi")
            || lower.Contains("em có câu hỏi")
            || lower.Contains("em co cau hoi")
            || lower.Contains("mình có câu hỏi")
            || lower.Contains("minh co cau hoi")
            || lower.Contains("bạn có thể")
            || lower.Contains("ban co the");
    }

    private static bool IsHintRequestMessage(string message)
    {
        var lower = message.Trim().ToLowerInvariant();
        return lower.Contains("hint")
            || lower.Contains("gợi ý")
            || lower.Contains("goi y")
            || lower.Contains("từ khóa")
            || lower.Contains("tu khoa")
            || lower.Contains("give me a hint")
            || lower.Contains("need a hint");
    }

    private static string BuildClarificationStatusSummary(string language, bool redirectedToQuestion) =>
        string.Equals(language, "VI", StringComparison.OrdinalIgnoreCase)
            ? redirectedToQuestion
                ? "Interviewer đã phản hồi ngắn gọn và yêu cầu ứng viên quay lại câu hỏi đang phỏng vấn."
                : "Interviewer đã phản hồi câu hỏi của ứng viên. Bạn có thể tiếp tục trả lời."
            : redirectedToQuestion
                ? "Interviewer answered briefly and asked the candidate to refocus on the active interview question."
                : "Interviewer answered the candidate's question. Please continue with your answer.";

    private static string BuildLanguageSelectionPrompt()
    {
        return "Before we start the interview, would you like to continue in English or Vietnamese? / Trước khi bắt đầu, bạn muốn phỏng vấn bằng tiếng Anh hay tiếng Việt?";
    }

    private static string BuildLanguageSelectionAcknowledgement(HiringSessionState state)
    {
        return string.Equals(state.ConversationLanguage, "VI", StringComparison.OrdinalIgnoreCase)
            ? "Cảm ơn bạn. Mình sẽ tiếp tục buổi phỏng vấn hoàn toàn bằng tiếng Việt từ đây."
            : "Thank you. We will continue the interview entirely in English from here.";
    }

    private static string AppendFocusReminder(HiringSessionState state, string interviewerReply)
    {
        var reminder = BuildFocusReminder(state);
        return string.IsNullOrWhiteSpace(interviewerReply)
            ? reminder
            : $"{interviewerReply} {reminder}";
    }

    private static string BuildFocusReminder(HiringSessionState state)
    {
        var activeQuestion = GetActiveInterviewQuestionText(state);
        return IsVietnamese(state)
            ? $"Mình muốn giữ nhịp buổi phỏng vấn, nên bây giờ quay lại câu hỏi chính nhé: {activeQuestion}"
            : $"I want to keep the interview focused, so let's come back to the main question now: {activeQuestion}";
    }

    private static string GetActiveInterviewQuestionText(HiringSessionState state)
    {
        if (state.ActiveQuestion is null)
            return state.CurrentPrompt;

        if (state.FollowUpAsked && !string.IsNullOrWhiteSpace(state.ActiveQuestion.FollowUpText))
            return state.ActiveQuestion.FollowUpText!;

        return state.ActiveQuestion.Text;
    }

    private static string BuildPanelIntroduction(HiringSessionState state, string speaker)
    {
        if (IsVietnamese(state))
        {
            return speaker switch
            {
                "HR" => "Chào bạn, tôi là HR interviewer. Tôi sẽ theo dõi cách giao tiếp và ghi lại interview notes.",
                "PM" => $"Chào bạn, tôi là PM interviewer. Bối cảnh dự án của buổi hôm nay là: {state.Request.ProjectBrief}",
                "BA" => "Chào bạn, tôi là BA interviewer. Tôi sẽ hỏi về cách bạn làm rõ yêu cầu, xử lý thay đổi và phối hợp với stakeholder.",
                _ => $"Chào bạn, tôi là {speaker} interviewer. Tôi sẽ đi sâu vào phần năng lực kỹ thuật phù hợp với vai trò này."
            };
        }

        return speaker switch
        {
            "HR" => "Hello, I am the HR interviewer. I will screen communication signals and keep the interview notes.",
            "PM" => $"Hello, I am the PM interviewer. Our project brief is: {state.Request.ProjectBrief}",
            "BA" => "Hello, I am the BA interviewer. I will ask about stakeholder scenarios and requirement handling.",
            _ => $"Hello, I am the {speaker} interviewer. I will focus on the technical depth relevant to this role."
        };
    }

    private static bool IsVietnamese(HiringSessionState state) => string.Equals(state.ConversationLanguage, "VI", StringComparison.OrdinalIgnoreCase);

    private async Task MoveToNextQuestionAsync(HiringSessionState state, CancellationToken cancellationToken)
    {
        WriteNotesFile(state);

        if (state.PendingQuestions.Count == 0)
            return;

        var question = state.PendingQuestions.Dequeue();

        state.ActiveQuestion = question;
        state.FollowUpAsked = false;
        state.HintCountForCurrentQuestion = 0;
        state.CandidateRequestCountForCurrentQuestion = 0;
        state.WaitingForClarificationAnswer = false;
        state.AwaitingLanguageSelection = false;
        state.CurrentSpeaker = question.Speaker;
        state.CurrentPrompt = question.Text;
        AddTurn(state, question.Speaker, question.Text);
        AppendQaEntry(state, question.Speaker, question.Text);
    }

    private void CompleteInterview(HiringSessionState state, string completionReason)
    {
        state.Stage = CompletedStage;
        state.RequiresUserApproval = false;
        state.ApprovalType = string.Empty;
        state.CurrentSpeaker = "HR";
        state.CurrentPrompt = IsVietnamese(state)
            ? "Cảm ơn bạn đã tham gia. Bên mình sẽ tổng hợp notes và phản hồi bước tiếp theo sau khi review."
            : "Thank you for your time. We are moving into closing and will follow up after reviewing the notes.";
        state.StatusSummary = completionReason;
        AddTurn(state, "PM", IsVietnamese(state)
            ? "Phần trao đổi từ phía bên mình tạm kết thúc tại đây. Cảm ơn bạn đã chia sẻ."
            : "That concludes the interview on our side. Thank you for the discussion.");
        AddTurn(state, "HR", IsVietnamese(state)
            ? "Bên mình sẽ đóng buổi interview tại đây và cập nhật bước tiếp theo sau khi review nội bộ. Cảm ơn bạn."
            : "We are now closing the session. Thank you, and we will share next steps after the review.");
        WriteNotesFile(state);
    }

    private void CreateCandidateFolder(HiringSessionState state)
    {
        try
        {
            var folderName = $"candidate-{state.SessionId:N}";
            var folderPath = Path.Combine(_notesRootPath, folderName);
            Directory.CreateDirectory(folderPath);
            state.CandidateFolder = folderPath;

            // jd-keywords.md
            var jdKeywords = ExtractKeywords(state.Request.JobDescription).OrderBy(k => k).ToList();
            var cvKeywords = ExtractKeywords(state.Request.CandidateCv).OrderBy(k => k).ToList();
            var matched = jdKeywords.Intersect(cvKeywords, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();

            var jdContent = new StringBuilder();
            jdContent.AppendLine("# JD Keywords");
            jdContent.AppendLine();
            jdContent.AppendLine($"SessionId: {state.SessionId}");
            jdContent.AppendLine($"SeniorityLevel: {state.SeniorityLevel}");
            jdContent.AppendLine($"TechnicalInterviewRole: {state.TechnicalInterviewRole}");
            jdContent.AppendLine();
            jdContent.AppendLine("## All keywords");
            foreach (var kw in jdKeywords) jdContent.AppendLine($"- {kw}");
            jdContent.AppendLine();
            jdContent.AppendLine("## Original JD");
            jdContent.AppendLine();
            jdContent.AppendLine(state.Request.JobDescription);
            File.WriteAllText(Path.Combine(folderPath, "jd-keywords.md"), jdContent.ToString());

            var cvContent = new StringBuilder();
            cvContent.AppendLine("# CV Keywords");
            cvContent.AppendLine();
            cvContent.AppendLine($"SessionId: {state.SessionId}");
            cvContent.AppendLine($"SeniorityLevel: {state.SeniorityLevel}");
            cvContent.AppendLine();
            cvContent.AppendLine("## Matched with JD");
            foreach (var kw in matched) cvContent.AppendLine($"- {kw}");
            cvContent.AppendLine();
            cvContent.AppendLine("## All CV keywords");
            foreach (var kw in cvKeywords) cvContent.AppendLine($"- {kw}");
            cvContent.AppendLine();
            cvContent.AppendLine("## Original CV");
            cvContent.AppendLine();
            cvContent.AppendLine(state.Request.CandidateCv);
            File.WriteAllText(Path.Combine(folderPath, "cv-keywords.md"), cvContent.ToString());

            // qa.md — will be appended during the interview
            state.QaFilePath = Path.Combine(folderPath, "interview-qa.md");
            var qaHeader = new StringBuilder();
            qaHeader.AppendLine("# Interview Q&A");
            qaHeader.AppendLine();
            qaHeader.AppendLine($"SessionId: {state.SessionId}");
            qaHeader.AppendLine($"ProjectBrief: {state.Request.ProjectBrief}");
            qaHeader.AppendLine($"SeniorityLevel: {state.SeniorityLevel}");
            qaHeader.AppendLine($"ConversationLanguage: {state.ConversationLanguage}");
            qaHeader.AppendLine($"TechnicalInterviewRole: {state.TechnicalInterviewRole}");
            qaHeader.AppendLine($"Started: {DateTimeOffset.UtcNow:O}");
            qaHeader.AppendLine();
            qaHeader.AppendLine("---");
            qaHeader.AppendLine();
            File.WriteAllText(state.QaFilePath, qaHeader.ToString());

            _logger.LogInformation("[HiringWorkflow] Created candidate folder {Folder} for session {SessionId}", folderPath, state.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HiringWorkflow] Could not create candidate folder for session {SessionId}", state.SessionId);
        }
    }

    private static void AppendQaEntry(HiringSessionState state, string speaker, string content)
    {
        if (string.IsNullOrWhiteSpace(state.QaFilePath))
            return;

        try
        {
            var entry = new StringBuilder();
            entry.AppendLine($"### [{DateTimeOffset.UtcNow:HH:mm:ss}] {speaker}");
            entry.AppendLine();
            entry.AppendLine(content);
            entry.AppendLine();
            File.AppendAllText(state.QaFilePath, entry.ToString());
        }
        catch
        {
            // Non-fatal: interview continues even if file write fails
        }
    }

    private void WriteNotesFile(HiringSessionState state)
    {
        var folder = string.IsNullOrWhiteSpace(state.CandidateFolder) ? _notesRootPath : state.CandidateFolder;
        var filePath = Path.Combine(folder, $"hiring-session-{state.SessionId:N}.md");
        var markdown = new StringBuilder();
        markdown.AppendLine("# Hiring Interview Notes");
        markdown.AppendLine();
        markdown.AppendLine($"- SessionId: {state.SessionId}");
        markdown.AppendLine($"- Stage: {state.Stage}");
        markdown.AppendLine($"- ProjectBrief: {state.Request.ProjectBrief}");
        markdown.AppendLine($"- SeniorityLevel: {state.SeniorityLevel}");
        markdown.AppendLine($"- ConversationLanguage: {state.ConversationLanguage}");
        markdown.AppendLine($"- TechnicalInterviewRole: {state.TechnicalInterviewRole}");
        markdown.AppendLine($"- ScreeningFitScore: {state.ScreeningFitScore:F1}");
        markdown.AppendLine($"- InterviewScore: {state.InterviewScore:F1}");
        if (!string.IsNullOrWhiteSpace(state.CandidateFolder))
            markdown.AppendLine($"- CandidateFolder: {state.CandidateFolder}");
        markdown.AppendLine();
        markdown.AppendLine("## Job Description");
        markdown.AppendLine();
        markdown.AppendLine(state.Request.JobDescription);
        markdown.AppendLine();
        markdown.AppendLine("## Candidate CV");
        markdown.AppendLine();
        markdown.AppendLine(state.Request.CandidateCv);
        markdown.AppendLine();
        markdown.AppendLine("## Transcript");
        markdown.AppendLine();
        foreach (var turn in state.Transcript)
        {
            markdown.AppendLine($"### {turn.Speaker}");
            markdown.AppendLine();
            markdown.AppendLine(turn.Message);
            markdown.AppendLine();
        }

        File.WriteAllText(filePath, markdown.ToString());
        state.NotesDocumentPath = filePath;
        _logger.LogInformation("[HiringWorkflow] Wrote interview notes for session {SessionId} to {Path}", state.SessionId, filePath);
    }

    private HiringSessionState GetRequiredSession(Guid sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var state))
            throw new KeyNotFoundException($"Hiring session '{sessionId}' was not found.");

        return state;
    }

    private static HiringSessionResult ToResult(HiringSessionState state) =>
        new(
            state.SessionId,
            state.Stage,
            state.RequiresUserApproval,
            state.ApprovalType,
            state.ScreeningFitScore,
            state.InterviewScore,
            state.CurrentSpeaker,
            state.CurrentPrompt,
            state.StatusSummary,
            state.SeniorityLevel,
            state.TechnicalInterviewRole,
            state.NotesDocumentPath,
            state.Participants.ToArray(),
            state.Transcript.ToArray(),
            FollowUpAvailable: state.ActiveQuestion?.FollowUpText != null && !state.FollowUpAsked,
            PendingFollowUp: state.ActiveQuestion?.FollowUpText,
            CandidateFolder: state.CandidateFolder);

    private static void AddTurn(HiringSessionState state, string speaker, string message)
    {
        state.Transcript.Add(new HiringTranscriptTurn(speaker, message, DateTimeOffset.UtcNow));
    }

    private static string ResolveNotesRootPath()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var docsPath = Path.Combine(current.FullName, "docs");
            if (Directory.Exists(docsPath))
                return Path.Combine(docsPath, "interviews");

            current = current.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "interview-notes");
    }

    private sealed class HiringSessionState
    {
        public HiringSessionState(HiringSessionStartRequest request, string technicalInterviewRole)
        {
            Request = request;
            TechnicalInterviewRole = technicalInterviewRole;
        }

        public Guid SessionId { get; } = Guid.NewGuid();
        public HiringSessionStartRequest Request { get; }
        public string Stage { get; set; } = "created";
        public bool RequiresUserApproval { get; set; }
        public string ApprovalType { get; set; } = string.Empty;
        public double ScreeningFitScore { get; set; }
        public string ScreeningSummary { get; set; } = string.Empty;
        public List<string> ScreeningStrengths { get; } = [];
        public List<string> ScreeningGaps { get; } = [];
        public double InterviewScore { get; set; }
        public string CurrentSpeaker { get; set; } = string.Empty;
        public string CurrentPrompt { get; set; } = string.Empty;
        public string StatusSummary { get; set; } = string.Empty;
        public string SeniorityLevel { get; set; } = "MID";
        public string ConversationLanguage { get; set; } = "EN";
        public string TechnicalInterviewRole { get; }
        public string NotesDocumentPath { get; set; } = string.Empty;
        public string CandidateFolder { get; set; } = string.Empty;
        public string QaFilePath { get; set; } = string.Empty;
        public int CandidateResponseCount { get; set; }
        public List<double> AnswerScores { get; } = [];
        public List<string> Participants { get; } = [];
        public List<HiringTranscriptTurn> Transcript { get; } = [];
        public Queue<InterviewQuestion> PendingQuestions { get; } = new();
        // Follow-up tracking for the active question
        public InterviewQuestion? ActiveQuestion { get; set; }
        public bool FollowUpAsked { get; set; }
        public int HintCountForCurrentQuestion { get; set; }
        public int CandidateRequestCountForCurrentQuestion { get; set; }
        public bool AwaitingLanguageSelection { get; set; }
        public bool InterviewLanguageLocked { get; set; }
        public bool WaitingForClarificationAnswer { get; set; }
        public string? PendingClarificationFrom { get; set; }
    }
}