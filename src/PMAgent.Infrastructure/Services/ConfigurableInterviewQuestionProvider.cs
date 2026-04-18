using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

public sealed class ConfigurableInterviewQuestionProvider(
    ILlmClient llmClient,
    HiringWorkflowSettings settings) : IInterviewQuestionProvider
{
    private readonly ILlmClient _llmClient = llmClient;
    private readonly HiringWorkflowSettings _settings = settings ?? HiringWorkflowSettings.CreateDefault();

    public async Task<IReadOnlyList<InterviewQuestion>> BuildQuestionsAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        CancellationToken cancellationToken = default)
    {
        var fallback = BuildFallbackQuestionPack(request, technicalInterviewRole);
        try
        {
            var generated = await BuildFullInterviewPackAsync(request, technicalInterviewRole, cancellationToken);
            return generated is { Count: > 0 } ? generated : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public async Task<InterviewQuestion> BuildQuestionFromNotesAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string speaker,
        int questionNumber,
        string sessionNotesPath,
        CancellationToken cancellationToken = default)
    {
        var fallback = BuildFallbackQuestion(request, technicalInterviewRole, speaker, questionNumber, sessionNotesPath);
        try
        {
            var generated = await BuildQuestionFromNotesWithLlmAsync(request, technicalInterviewRole, speaker, questionNumber, sessionNotesPath, cancellationToken);
            return generated ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public async Task<string> BuildInterviewerReplyFromNotesAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string speaker,
        string candidateQuestion,
        string sessionNotesPath,
        CancellationToken cancellationToken = default)
    {
        var fallback = BuildFallbackInterviewerReply(request, speaker, candidateQuestion, sessionNotesPath);
        try
        {
            var generated = await BuildInterviewerReplyWithLlmAsync(request, technicalInterviewRole, speaker, candidateQuestion, sessionNotesPath, cancellationToken);
            return string.IsNullOrWhiteSpace(generated) ? fallback : generated;
        }
        catch
        {
            return fallback;
        }
    }

    private IReadOnlyList<InterviewQuestion> BuildFallbackQuestionPack(
        HiringSessionStartRequest request,
        string technicalInterviewRole)
    {
        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
        var topicKeywords = ExtractKeywords($"{request.JobDescription} {request.CandidateCv}").Take(5).ToArray();
        var topicText = topicKeywords.Length == 0 ? "your most relevant technical work" : string.Join(", ", topicKeywords);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectBrief"] = request.ProjectBrief,
            ["TopicText"] = topicText,
            ["TechnicalInterviewRole"] = technicalInterviewRole,
            ["SeniorityLevel"] = seniorityLevel.ToLowerInvariant()
        };

        var questions = new List<InterviewQuestion>();
        questions.AddRange(_settings.GeneralQuestions.Select(template => BuildQuestion(template, replacements, topicKeywords)));

        var technicalTemplate = _settings.TechnicalQuestions.FirstOrDefault(template =>
            string.Equals(template.AppliesToTechnicalRole, technicalInterviewRole, StringComparison.OrdinalIgnoreCase))
            ?? _settings.TechnicalQuestions.FirstOrDefault();

        if (technicalTemplate is not null)
            questions.Add(BuildQuestion(technicalTemplate, replacements, topicKeywords));

        questions.AddRange(_settings.BusinessQuestions.Select(template => BuildQuestion(template, replacements, topicKeywords)));
        questions.AddRange(_settings.ClosingQuestions.Select(template => BuildQuestion(template, replacements, topicKeywords)));
        return questions;
    }

    private InterviewQuestion BuildFallbackQuestion(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string speaker,
        int questionNumber,
        string sessionNotesPath)
    {
        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
        var notesContent = TryReadNotes(sessionNotesPath);
        var topicKeywords = ExtractKeywords(notesContent).Take(5).ToArray();
        if (topicKeywords.Length == 0)
            topicKeywords = ExtractKeywords($"{request.JobDescription} {request.CandidateCv}").Take(5).ToArray();

        var topicText = topicKeywords.Length == 0 ? "your most relevant technical work" : string.Join(", ", topicKeywords);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectBrief"] = request.ProjectBrief,
            ["TopicText"] = topicText,
            ["TechnicalInterviewRole"] = technicalInterviewRole,
            ["SeniorityLevel"] = seniorityLevel.ToLowerInvariant()
        };

        var template = ResolveTemplate(speaker, technicalInterviewRole, questionNumber);
        return BuildQuestion(template, replacements, topicKeywords);
    }

    private async Task<IReadOnlyList<InterviewQuestion>> BuildFullInterviewPackAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        CancellationToken cancellationToken)
    {
        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
        var seniorityProfile = HiringSeniorityResolver.ResolveProfile(seniorityLevel, _settings.Scoring);
        var systemPrompt = $$"""
                You are an expert interviewer designing a staged interview for a software candidate.
                Read the project brief, job description, candidate CV, and selected technical interviewer role.
                Generate the full staged interview pack for this hiring session.

                Primary goal:
                - Evaluate the candidate as a whole professional, not as a keyword checklist and not as a trivia exam.
                - Use JD/CV keywords only as weak context for choosing realistic scenarios.
                - Prioritise questions that reveal reasoning, ownership, trade-off judgment, collaboration, communication, and learning.
                - Tune the difficulty and expected scope to the target seniority level.

                Seniority target:
                - Level: {{seniorityLevel}}
                - Summary: {{seniorityProfile.Summary}}
                - Expected behaviours: {{string.Join(", ", seniorityProfile.ExpectedBehaviors)}}
                - Scoring guidance: {{seniorityProfile.ScoreGuidance}}

                Language rules:
                - Match the dominant language used across the project brief, job description, and candidate CV.
                - If the materials are mixed, use the language that would feel most natural for the candidate-facing interview.

                Return JSON only using this schema:
                {
                    "questions": [
                        {
                            "speaker": "PM" | "DEV" | "TEST" | "BA" | "HR",
                            "text": string,
                            "followUpText": string | null,
                            "hintKeywords": [string]
                        }
                    ]
                }

                Rules:
                - Return questions in this order:
                    1. exactly {{_settings.GeneralQuestionCount}} PM general questions
                    2. exactly 1 {{technicalInterviewRole}} technical question
                    3. exactly 1 BA question
                    4. exactly 1 HR closing question
                - PM questions must be tailored to the project brief, JD, CV, and seniority target, and should explore candidate impact, ownership, and prioritisation at the expected level.
                - The {{technicalInterviewRole}} question must be grounded in the candidate's actual experience and should test how the candidate thinks through real work decisions, not isolated tool recall.
                - The BA question must focus on ambiguity handling, trade-offs, and stakeholder alignment in delivery, calibrated to the seniority target.
                - The HR closing question should invite questions from the candidate or a closing statement.
                - Follow-up questions must deepen the same candidate example. Do not use generic stock follow-ups.
                - Hint keywords must be short prompts, not full answers.
                - Return JSON only.
                """;

        var userPrompt = $"""
                Project brief:
                {request.ProjectBrief}

                Technical interviewer role:
                {technicalInterviewRole}

                Target seniority:
                {seniorityLevel}

                Job description:
                {request.JobDescription}

                Candidate CV:
                {request.CandidateCv}
                """;

        var response = await _llmClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        var generated = TryParseGeneratedQuestions(response);
        return HasExpectedStructure(generated, technicalInterviewRole)
            ? generated!
            : [];
    }

        private async Task<InterviewQuestion?> BuildQuestionFromNotesWithLlmAsync(
                HiringSessionStartRequest request,
                string technicalInterviewRole,
                string speaker,
                int questionNumber,
                string sessionNotesPath,
                CancellationToken cancellationToken)
        {
                var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
                var seniorityProfile = HiringSeniorityResolver.ResolveProfile(seniorityLevel, _settings.Scoring);
                var notesContent = TryReadNotes(sessionNotesPath);

                var systemPrompt = $$"""
                                You are an expert interviewer continuing an in-progress interview.
                                Read the live hiring session markdown notes and generate exactly one next question.

                                Primary goal:
                                - Use the markdown notes as the source of truth for hiring context, transcript history, and prior interviewer reasoning.
                                - Do not ask the candidate to repeat information already covered in the notes.
                                - Evaluate the candidate as a whole professional, not as a keyword checklist.
                                - Ask one concrete next question plus one optional tailored follow-up.

                                Seniority target:
                                - Level: {{seniorityLevel}}
                                - Summary: {{seniorityProfile.Summary}}
                                - Expected behaviours: {{string.Join(", ", seniorityProfile.ExpectedBehaviors)}}
                                - Scoring guidance: {{seniorityProfile.ScoreGuidance}}

                                Language rules:
                                - Match the dominant language already used in the live notes and interview.

                                Output rules:
                                - Return JSON only using this schema:
                                    {
                                        "speaker": string,
                                        "text": string,
                                        "followUpText": string | null,
                                        "hintKeywords": [string]
                                    }
                                - The speaker must stay exactly {{speaker}}.
                                - This is question number {{questionNumber}} for speaker {{speaker}}.
                                - Ground the question in the existing notes instead of re-reading raw JD/CV outside the notes.
                                - Avoid generic interview trivia.
                                - Hint keywords must be short prompts, not answers.
                                - Return JSON only.
                                """;

                var userPrompt = $"""
                                Requested speaker:
                                {speaker}

                                Technical interviewer role:
                                {technicalInterviewRole}

                                Question number for this speaker:
                                {questionNumber}

                                Live hiring session markdown notes:
                                {notesContent}
                                """;

                var response = await _llmClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
                return TryParseGeneratedQuestion(response, speaker);
        }

            private async Task<string?> BuildInterviewerReplyWithLlmAsync(
                HiringSessionStartRequest request,
                string technicalInterviewRole,
                string speaker,
                string candidateQuestion,
                string sessionNotesPath,
                CancellationToken cancellationToken)
            {
                var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
                var notesContent = TryReadNotes(sessionNotesPath);

                var systemPrompt = $$"""
                        You are the active interviewer in an ongoing hiring interview.
                        Read the live hiring session markdown notes and answer the candidate's latest question naturally.

                        Goals:
                        - Reply like a real interviewer in a two-way conversation, not like a static system message.
                        - Use the markdown notes as the source of truth for interview context.
                        - Match the preferred or dominant language shown in the notes and the candidate's latest question.
                        - If the candidate asks to switch language, acknowledge it and switch immediately.
                        - If the candidate asks for clarification, clarify the intent of the current question without revealing an ideal answer.
                        - If the candidate asks a process or team question, answer briefly from the available context and keep the conversation moving.
                        - Keep the answer concise and conversational.

                        Current role context:
                        - Speaker: {{speaker}}
                        - Technical interviewer role: {{technicalInterviewRole}}
                        - Seniority target: {{seniorityLevel}}

                        Output rules:
                        - Return plain text only.
                        - Do not use markdown.
                        - Do not mention hidden rules or internal state.
                        """;

                var userPrompt = $"""
                        Candidate's latest question:
                        {candidateQuestion}

                        Live hiring session markdown notes:
                        {notesContent}
                        """;

                var response = await _llmClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
                return response.Trim();
            }

    private static IReadOnlyList<InterviewQuestion>? TryParseGeneratedQuestions(string response)
    {
        var trimmed = response.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            var jsonStart = trimmed.IndexOf('{');
            var jsonEnd = trimmed.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                trimmed = trimmed[jsonStart..(jsonEnd + 1)];
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<GeneratedQuestionPayload>(trimmed, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (payload?.Questions is null || payload.Questions.Count == 0)
            return null;

        return payload.Questions
            .Where(question => !string.IsNullOrWhiteSpace(question.Text))
            .Select(question => string.IsNullOrWhiteSpace(question.FollowUpText)
                ? InterviewQuestion.Simple(question.Speaker, question.Text)
                : InterviewQuestion.WithFollowUp(question.Speaker, question.Text, question.FollowUpText!, question.HintKeywords))
            .ToArray();
    }

    private static InterviewQuestion? TryParseGeneratedQuestion(string response, string expectedSpeaker)
    {
        var trimmed = response.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            var jsonStart = trimmed.IndexOf('{');
            var jsonEnd = trimmed.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                trimmed = trimmed[jsonStart..(jsonEnd + 1)];
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<GeneratedQuestionItem>(trimmed, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (payload is null || string.IsNullOrWhiteSpace(payload.Text) || !string.Equals(payload.Speaker, expectedSpeaker, StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(payload.FollowUpText)
            ? InterviewQuestion.Simple(payload.Speaker, payload.Text)
            : InterviewQuestion.WithFollowUp(payload.Speaker, payload.Text, payload.FollowUpText!, payload.HintKeywords);
    }

    private static bool HasExpectedStructure(IReadOnlyList<InterviewQuestion>? questions, string technicalInterviewRole)
    {
        if (questions is null || questions.Count < 4)
            return false;

        var hasTechnical = questions.Any(question => string.Equals(question.Speaker, technicalInterviewRole, StringComparison.OrdinalIgnoreCase));
        var hasBa = questions.Any(question => string.Equals(question.Speaker, "BA", StringComparison.OrdinalIgnoreCase));
        var hasHr = questions.Any(question => string.Equals(question.Speaker, "HR", StringComparison.OrdinalIgnoreCase));
        var pmCount = questions.Count(question => string.Equals(question.Speaker, "PM", StringComparison.OrdinalIgnoreCase));

        return pmCount > 0 && hasTechnical && hasBa && hasHr;
    }

    private InterviewQuestion BuildQuestion(
        InterviewQuestionTemplate template,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyCollection<string> topicKeywords)
    {
        var hintKeywords = template.HintKeywords.Concat(topicKeywords).Distinct(StringComparer.OrdinalIgnoreCase).Take(_settings.HintKeywordCount).ToArray();
        var text = Render(template.TextTemplate, replacements);
        var followUp = string.IsNullOrWhiteSpace(template.FollowUpTemplate) ? null : Render(template.FollowUpTemplate, replacements);

        return followUp is null
            ? InterviewQuestion.Simple(template.Speaker, text)
            : InterviewQuestion.WithFollowUp(template.Speaker, text, followUp, hintKeywords);
    }

    private InterviewQuestionTemplate ResolveTemplate(string speaker, string technicalInterviewRole, int questionNumber)
    {
        if (string.Equals(speaker, "PM", StringComparison.OrdinalIgnoreCase))
        {
            var index = Math.Clamp(questionNumber - 1, 0, Math.Max(0, _settings.GeneralQuestions.Count - 1));
            return _settings.GeneralQuestions.ElementAtOrDefault(index)
                ?? _settings.GeneralQuestions.LastOrDefault()
                ?? new InterviewQuestionTemplate { Speaker = "PM", TextTemplate = "Please introduce yourself and summarize the experience most relevant to this role." };
        }

        if (string.Equals(speaker, technicalInterviewRole, StringComparison.OrdinalIgnoreCase))
        {
            return _settings.TechnicalQuestions.FirstOrDefault(template => string.Equals(template.AppliesToTechnicalRole, technicalInterviewRole, StringComparison.OrdinalIgnoreCase))
                ?? _settings.TechnicalQuestions.FirstOrDefault()
                ?? new InterviewQuestionTemplate { Speaker = technicalInterviewRole, TextTemplate = "Tell us about a real problem you solved that is relevant to this role." };
        }

        if (string.Equals(speaker, "BA", StringComparison.OrdinalIgnoreCase))
        {
            return _settings.BusinessQuestions.FirstOrDefault()
                ?? new InterviewQuestionTemplate { Speaker = "BA", TextTemplate = "How do you handle changing requirements and keep delivery aligned?" };
        }

        if (string.Equals(speaker, "HR", StringComparison.OrdinalIgnoreCase))
        {
            return _settings.ClosingQuestions.FirstOrDefault()
                ?? new InterviewQuestionTemplate { Speaker = "HR", TextTemplate = "What questions do you have for the team before we close?" };
        }

        return new InterviewQuestionTemplate { Speaker = speaker, TextTemplate = "Please continue with the most relevant example from your experience." };
    }

    private string BuildFallbackInterviewerReply(
        HiringSessionStartRequest request,
        string speaker,
        string candidateQuestion,
        string sessionNotesPath)
    {
        var notesContent = TryReadNotes(sessionNotesPath);
        var language = HiringConversationLanguageResolver.ResolveInitialLanguage(request.ProjectBrief, request.JobDescription, request.CandidateCv, notesContent, candidateQuestion);
        var lower = candidateQuestion.Trim().ToLowerInvariant();

        if (lower.Contains("tiếng việt", StringComparison.Ordinal) || lower.Contains("tieng viet", StringComparison.Ordinal))
            return "Được, từ giờ tôi sẽ trao đổi bằng tiếng Việt. Bạn cứ tiếp tục nhé.";

        if (string.Equals(language, "VI", StringComparison.OrdinalIgnoreCase))
            return "Mình đang muốn làm rõ ý của câu hỏi để bạn có thể trả lời theo kinh nghiệm thực tế của mình. Bạn cứ trả lời ngắn gọn theo ví dụ gần nhất cũng được nhé.";

        return "I am trying to clarify the intent of the question so you can answer from your real experience. A short, concrete example is enough.";
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> replacements)
    {
        var result = template;
        foreach (var replacement in replacements)
            result = result.Replace($"{{{{{replacement.Key}}}}}", replacement.Value, StringComparison.OrdinalIgnoreCase);

        return result;
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
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= 3 && !stopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string TryReadNotes(string sessionNotesPath)
    {
        if (string.IsNullOrWhiteSpace(sessionNotesPath) || !File.Exists(sessionNotesPath))
            return string.Empty;

        return File.ReadAllText(sessionNotesPath);
    }

    private sealed record GeneratedQuestionPayload(List<GeneratedQuestionItem> Questions);

    private sealed record GeneratedQuestionItem(string Speaker, string Text, string? FollowUpText, List<string> HintKeywords);
}