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
    private const double ScreeningPassThreshold = 70;
    private const int MinimumResponsesBeforeEarlyStop = 2;

    private static readonly string[] SupportedTechnicalRoles = ["DEV", "TEST"];
    private static readonly ConcurrentDictionary<Guid, HiringSessionState> Sessions = new();

    private readonly IInterviewScoringAgent _scoringAgent;
    private readonly ILogger<InMemoryHiringWorkflowService> _logger;
    private readonly string _notesRootPath;

    public InMemoryHiringWorkflowService(
        IInterviewScoringAgent scoringAgent,
        ILogger<InMemoryHiringWorkflowService> logger,
        string? notesRootPath = null)
    {
        _scoringAgent = scoringAgent;
        _logger = logger;
        _notesRootPath = notesRootPath ?? ResolveNotesRootPath();
        Directory.CreateDirectory(_notesRootPath);
    }

    public Task<HiringSessionResult?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Sessions.TryGetValue(sessionId, out var state) ? ToResult(state) : null);
    }

    public Task<HiringSessionResult> StartAsync(HiringSessionStartRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var technicalRole = NormalizeTechnicalRole(request.TechnicalInterviewRole);
        var state = new HiringSessionState(request, technicalRole);
        state.ScreeningFitScore = CalculateFitScore(request.JobDescription, request.CandidateCv);
        state.Participants.Add("HR");

        // Create per-candidate folder and write keyword files for reuse during the interview
        CreateCandidateFolder(state);

        var screeningSummary = BuildScreeningSummary(request, state.ScreeningFitScore, technicalRole);
        AddTurn(state, "HR", screeningSummary);

        if (state.ScreeningFitScore < ScreeningPassThreshold)
        {
            state.Stage = RejectedStage;
            state.StatusSummary = "HR screening rejected the candidate because fit is below the threshold.";
            state.CurrentSpeaker = "HR";
            state.CurrentPrompt = "The screening fit is below 70%, so the process stops here.";
            WriteNotesFile(state);
            return Task.FromResult(ToResult(state));
        }

        state.Stage = AwaitingScreeningApprovalStage;
        state.RequiresUserApproval = true;
        state.ApprovalType = "screening_forward";
        state.CurrentSpeaker = "HR";
        state.CurrentPrompt = $"The CV fit score is {state.ScreeningFitScore:F1}%. Do you approve forwarding this CV to PM, BA, and {technicalRole}?";
        state.StatusSummary = "HR screening passed. Waiting for user approval to forward the CV.";

        Sessions[state.SessionId] = state;
        _logger.LogInformation("[HiringWorkflow] Started session {SessionId} with screening fit {FitScore}", state.SessionId, state.ScreeningFitScore);
        return Task.FromResult(ToResult(state));
    }

    public Task<HiringSessionResult> ApproveScreeningAsync(Guid sessionId, HiringApprovalRequest request, CancellationToken cancellationToken = default)
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
            return Task.FromResult(ToResult(state));
        }

        state.Participants.Clear();
        state.Participants.AddRange(["HR", "PM", "BA", state.TechnicalInterviewRole]);

        var schedulingMessage = BuildSchedulingMessage(state);
        AddTurn(state, "PM", schedulingMessage);

        if (state.Request.AutoApproveInterviewSchedule)
        {
            AddTurn(state, "USER", "Interview schedule approved automatically by default.");
            BeginInterview(state);
            state.StatusSummary = "Interview schedule auto-approved. Interview started immediately.";
            return Task.FromResult(ToResult(state));
        }

        state.Stage = AwaitingInterviewApprovalStage;
        state.RequiresUserApproval = true;
        state.ApprovalType = "interview_schedule";
        state.CurrentSpeaker = "PM";
        state.CurrentPrompt = "PM has prepared the interview schedule. Do you approve starting the interview?";
        state.StatusSummary = "Waiting for user approval to start the interview.";
        return Task.FromResult(ToResult(state));
    }

    public Task<HiringSessionResult> ApproveInterviewScheduleAsync(Guid sessionId, HiringApprovalRequest request, CancellationToken cancellationToken = default)
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
            return Task.FromResult(ToResult(state));
        }

        BeginInterview(state);
        state.StatusSummary = "Interview approved. Panel introductions completed and candidate introduction requested.";
        return Task.FromResult(ToResult(state));
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

        // If the candidate is requesting a hint through the flag, redirect to hint logic
        if (request.IsHintRequest)
            return await RequestHintAsync(sessionId, cancellationToken);

        // Detect clarification question from the candidate
        if (IsClarificationOrQuestion(message) && !state.WaitingForClarificationAnswer)
        {
            // The interviewer answers the clarification; the same main question stays active
            state.WaitingForClarificationAnswer = true;
            state.PendingClarificationFrom = state.CurrentSpeaker;
            AddTurn(state, "CANDIDATE", message);
            AppendQaEntry(state, "CANDIDATE", message);

            var clarificationReply = BuildClarificationReply(state, message);
            state.CurrentPrompt = clarificationReply;
            AddTurn(state, state.CurrentSpeaker, clarificationReply);
            AppendQaEntry(state, state.CurrentSpeaker, clarificationReply);

            state.StatusSummary = "Interviewer answered the candidate's clarification. Please continue with your answer.";
            return ToResult(state);
        }

        // Candidate answered — record it
        state.WaitingForClarificationAnswer = false;
        AddTurn(state, "CANDIDATE", message);
        state.CandidateResponseCount++;
        AppendQaEntry(state, "CANDIDATE", message);
        AddTurn(state, "HR", BuildHrInterviewNote(state, message));

        // Evaluate score
        var score = await _scoringAgent.EvaluateAsync(
            state.Request.ProjectBrief,
            state.Request.JobDescription,
            state.TechnicalInterviewRole,
            state.Transcript,
            state.CandidateResponseCount,
            cancellationToken);

        state.InterviewScore = score.Score;
        AddTurn(state, "EVAL", score.Rationale);

        if (score.ShouldStop && state.CandidateResponseCount >= MinimumResponsesBeforeEarlyStop)
        {
            CompleteInterview(state, "Interview stopped early because the score dropped below the acceptable threshold.");
            return ToResult(state);
        }

        // Offer follow-up if the active question has one and it has not been asked yet
        if (state.ActiveQuestion?.FollowUpText is { } followUp && !state.FollowUpAsked)
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
        if (state.PendingSteps.Count == 0)
        {
            CompleteInterview(state, "Interview objectives have been covered. Moving to Q/A and closing.");
            return ToResult(state);
        }

        MoveToNextQuestion(state);
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
            var selectedHints = hints.Take(3).ToArray();
            hintText = $"Here are a few keywords that might help: {string.Join(", ", selectedHints)}. Take your time and try to explain what you know.";
        }
        else
        {
            // Derive hints from the matched JD/CV keywords stored in the candidate folder
            var derivedHints = ExtractKeywords(state.Request.JobDescription).Take(3).ToArray();
            hintText = derivedHints.Length > 0
                ? $"Consider these keywords from the job description: {string.Join(", ", derivedHints)}. Try to connect them to your experience."
                : "Think about the most relevant experience you have and how it applies to this question.";
        }

        state.HintCountForCurrentQuestion++;
        AddTurn(state, state.CurrentSpeaker, hintText);
        AppendQaEntry(state, state.CurrentSpeaker, $"[HINT] {hintText}");
        state.CurrentPrompt = hintText;
        state.StatusSummary = $"Hint provided. Hint count for this question: {state.HintCountForCurrentQuestion}.";

        return Task.FromResult(ToResult(state));
    }

    private static string NormalizeTechnicalRole(string technicalInterviewRole)
    {
        if (string.IsNullOrWhiteSpace(technicalInterviewRole))
            return "DEV";

        var normalized = technicalInterviewRole.Trim().ToUpperInvariant();
        return SupportedTechnicalRoles.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : "DEV";
    }

    private static double CalculateFitScore(string jobDescription, string candidateCv)
    {
        var jdKeywords = ExtractKeywords(jobDescription);
        var cvKeywords = ExtractKeywords(candidateCv);
        if (jdKeywords.Count == 0)
            return 0;

        var matches = jdKeywords.Intersect(cvKeywords, StringComparer.OrdinalIgnoreCase).Count();
        var keywordScore = (double)matches / jdKeywords.Count * 100;
        var capabilityScore = CalculateCapabilityScore(jobDescription, candidateCv);

        var evidenceBoost = Math.Min(20, CountEvidenceSignals(candidateCv) * 4);
        return Math.Clamp(Math.Max(keywordScore, capabilityScore) + evidenceBoost, 0, 100);
    }

    private static int CountEvidenceSignals(string candidateCv)
    {
        var lower = candidateCv.ToLowerInvariant();
        var signals = new[] { "built", "led", "implemented", "deployed", "automated", "optimized", "designed", "improved" };
        return signals.Count(lower.Contains);
    }

    private static double CalculateCapabilityScore(string jobDescription, string candidateCv)
    {
        var jdCapabilities = ExtractCapabilities(jobDescription);
        if (jdCapabilities.Count == 0)
            return 0;

        var cvCapabilities = ExtractCapabilities(candidateCv);
        var matches = jdCapabilities.Intersect(cvCapabilities, StringComparer.OrdinalIgnoreCase).Count();
        return (double)matches / jdCapabilities.Count * 100;
    }

    private static HashSet<string> ExtractCapabilities(string text)
    {
        var lower = text.ToLowerInvariant();
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCapability(capabilities, lower, "csharp", "c#", "csharp");
        AddCapability(capabilities, lower, "dotnet", ".net", "asp.net", "dotnet");
        AddCapability(capabilities, lower, "api", " api", "apis", "rest api", "restful");
        AddCapability(capabilities, lower, "postgresql", "postgresql", "postgres");
        AddCapability(capabilities, lower, "docker", "docker");
        AddCapability(capabilities, lower, "deployment", "deploy", "deployment", "production", "release", "cloud", "azure", "aws", "gcp", "pipeline", "devops", "ci/cd", "ci", "cd");
        AddCapability(capabilities, lower, "design", "design", "architecture", "architect");
        AddCapability(capabilities, lower, "testing", "test", "testing", "qa");
        AddCapability(capabilities, lower, "playwright", "playwright");
        AddCapability(capabilities, lower, "regression", "regression");
        AddCapability(capabilities, lower, "automation", "automation", "automated", "automate");
        AddCapability(capabilities, lower, "strategy", "strategy", "planning", "plan");

        return capabilities;
    }

    private static void AddCapability(HashSet<string> capabilities, string text, string capability, params string[] signals)
    {
        if (signals.Any(text.Contains))
            capabilities.Add(capability);
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

    private static string BuildScreeningSummary(HiringSessionStartRequest request, double fitScore, string technicalRole)
    {
        var matchedKeywords = ExtractKeywords(request.JobDescription)
            .Intersect(ExtractKeywords(request.CandidateCv), StringComparer.OrdinalIgnoreCase)
            .Take(8);

        return $"""
            HR screening summary

            - Fit score: {fitScore:F1}%
            - Target technical interviewer: {technicalRole}
            - Matched keywords: {string.Join(", ", matchedKeywords)}
            - Next action: {(fitScore >= ScreeningPassThreshold ? "Ask the user whether HR may forward the CV to PM, BA, and the technical interviewer." : "Stop the process because fit is below threshold.")}
            """;
    }

    private static string BuildSchedulingMessage(HiringSessionState state)
    {
        return $"""
            PM scheduling summary

            - Panel: PM, HR, BA, {state.TechnicalInterviewRole}
            - Flow: introductions -> candidate introduction -> PM project overview -> technical questions -> BA scenario -> Q/A -> closing
            - Default behavior: interview can start immediately unless the user wants to pause for schedule approval.
            - Required approval: confirm whether PM should proceed to the interview stage.
            """;
    }

    private void BeginInterview(HiringSessionState state)
    {
        state.Stage = InterviewActiveStage;
        state.RequiresUserApproval = false;
        state.ApprovalType = string.Empty;

        AddTurn(state, "HR", "Hello, I am the HR interviewer. I will screen communication signals and keep the interview notes.");
        AddTurn(state, "PM", $"Hello, I am the PM interviewer. Our project brief is: {state.Request.ProjectBrief}");
        AddTurn(state, state.TechnicalInterviewRole, $"Hello, I am the {state.TechnicalInterviewRole} interviewer. I will focus on the technical depth relevant to this role.");
        AddTurn(state, "BA", "Hello, I am the BA interviewer. I will ask about stakeholder scenarios and requirement handling.");

        state.PendingSteps.Clear();
        state.PendingSteps.Enqueue(InterviewQuestion.Simple("PM",
            "Please introduce yourself and summarize the experience most relevant to this role."));
        state.PendingSteps.Enqueue(InterviewQuestion.WithFollowUp("PM",
            $"Let me briefly introduce the project: {state.Request.ProjectBrief}. Based on this context, what part of the project would you expect to own or contribute to first?",
            "Can you elaborate on how you would approach the early days — what would you prioritise to make an impact quickly?",
            ["roadmap", "stakeholders", "onboarding", "quick wins", "delivery scope"]));
        state.PendingSteps.Enqueue(BuildTechnicalQuestion(state.TechnicalInterviewRole, state.Request.JobDescription, state.Request.CandidateCv));
        state.PendingSteps.Enqueue(InterviewQuestion.WithFollowUp("BA",
            BuildBaScenarioQuestionText(state.Request.ProjectBrief),
            "How do you document and communicate the impact of such a scope change to the broader team?",
            ["change log", "RACI", "impact assessment", "stakeholder update", "decision record"]));
        state.PendingSteps.Enqueue(InterviewQuestion.Simple("HR",
            "We are moving to Q/A. What questions do you have for the team, or what else would you like to add before we close?"));

        MoveToNextQuestion(state);
    }

    private static InterviewQuestion BuildTechnicalQuestion(string technicalRole, string jobDescription, string candidateCv)
    {
        var keywords = ExtractKeywords($"{jobDescription} {candidateCv}").Take(5).ToArray();
        var topicText = keywords.Length == 0 ? "your most relevant technical work" : string.Join(", ", keywords);
        var hintKeywords = keywords.Concat(ExtractKeywords(jobDescription).Take(3)).Distinct().Take(6).ToList();

        return technicalRole switch
        {
            "TEST" => InterviewQuestion.WithFollowUp(technicalRole,
                $"From a QA perspective, walk us through how you would design a test strategy for a system involving {topicText}. Focus on automation, regression, and release confidence.",
                "What metrics would you track to prove that release quality is improving iteration over iteration?",
                hintKeywords),
            _ => InterviewQuestion.WithFollowUp(technicalRole,
                $"From an engineering perspective, walk us through a technical decision you made involving {topicText}. Explain the trade-offs, architecture choices, and how you validated the result.",
                "If you had to make that decision again with 6 more months of hindsight, what would you change?",
                hintKeywords)
        };
    }

    private static string BuildBaScenarioQuestionText(string projectBrief) =>
        $"Imagine you joined the project '{projectBrief}' and a key stakeholder changed requirements late in the cycle. How would you clarify impact, negotiate priorities, and keep delivery aligned?";

    private static string BuildHrInterviewNote(HiringSessionState state, string candidateMessage)
    {
        var shortened = candidateMessage.Length <= 180 ? candidateMessage : $"{candidateMessage[..177]}...";
        return $"HR note: candidate response #{state.CandidateResponseCount} - {shortened}";
    }

    private static bool IsClarificationOrQuestion(string message)
    {
        var lower = message.Trim().ToLowerInvariant();
        return lower.EndsWith('?')
            || lower.Contains("what do you mean")
            || lower.Contains("could you clarify")
            || lower.Contains("can you explain")
            || lower.Contains("do you mean")
            || lower.Contains("what is meant by")
            || lower.StartsWith("does that mean");
    }

    private static string BuildClarificationReply(HiringSessionState state, string candidateQuestion)
    {
        // Build a context-aware clarification using the stored keywords from the candidate folder
        var jdKeywords = ExtractKeywords(state.Request.JobDescription).Take(5);
        return $"Good question. To clarify: the question focuses on {string.Join(", ", jdKeywords)} in the context of {state.Request.ProjectBrief}. Please continue with your answer when ready.";
    }

    private void MoveToNextQuestion(HiringSessionState state)
    {
        var question = state.PendingSteps.Dequeue();
        state.ActiveQuestion = question;
        state.FollowUpAsked = false;
        state.HintCountForCurrentQuestion = 0;
        state.WaitingForClarificationAnswer = false;
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
        state.CurrentPrompt = "Thank you for your time. We are moving into closing and will follow up after reviewing the notes.";
        state.StatusSummary = completionReason;
        AddTurn(state, "PM", "That concludes the interview on our side. Thank you for the discussion.");
        AddTurn(state, "HR", "We are now closing the session. Thank you, and we will share next steps after the review.");
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
        public double InterviewScore { get; set; }
        public string CurrentSpeaker { get; set; } = string.Empty;
        public string CurrentPrompt { get; set; } = string.Empty;
        public string StatusSummary { get; set; } = string.Empty;
        public string TechnicalInterviewRole { get; }
        public string NotesDocumentPath { get; set; } = string.Empty;
        public string CandidateFolder { get; set; } = string.Empty;
        public string QaFilePath { get; set; } = string.Empty;
        public int CandidateResponseCount { get; set; }
        public List<string> Participants { get; } = [];
        public List<HiringTranscriptTurn> Transcript { get; } = [];
        public Queue<InterviewQuestion> PendingSteps { get; } = new();
        // Follow-up tracking for the active question
        public InterviewQuestion? ActiveQuestion { get; set; }
        public bool FollowUpAsked { get; set; }
        public int HintCountForCurrentQuestion { get; set; }
        public bool WaitingForClarificationAnswer { get; set; }
        public string? PendingClarificationFrom { get; set; }
    }
}