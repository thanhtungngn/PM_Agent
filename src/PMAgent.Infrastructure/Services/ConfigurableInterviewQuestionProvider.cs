using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

public sealed class ConfigurableInterviewQuestionProvider(
    ILlmClient llmClient,
    HiringWorkflowSettings settings) : IInterviewQuestionProvider
{
    private static readonly string[] SkillCategoryPriority =
    [
        "programming_language",
        "framework",
        "system_design",
        "database",
        "delivery_methodology"
    ];

    private static readonly SkillSignal[] SkillCatalog =
    [
        new("programming_language", "C#", ["c#", "csharp"]),
        new("programming_language", ".NET", [".net", "dotnet"]),
        new("programming_language", "Java", ["java"]),
        new("programming_language", "Python", ["python"]),
        new("programming_language", "JavaScript", ["javascript"]),
        new("programming_language", "TypeScript", ["typescript", "ts"]),
        new("programming_language", "Go", ["golang", "go language", "go backend"]),
        new("programming_language", "PHP", ["php"]),
        new("programming_language", "Ruby", ["ruby"]),
        new("programming_language", "Kotlin", ["kotlin"]),

        new("framework", "ASP.NET Core", ["asp.net core", "asp net core"]),
        new("framework", "Entity Framework", ["entity framework", "ef core", "ef"]),
        new("framework", "Spring Boot", ["spring boot"]),
        new("framework", "Django", ["django"]),
        new("framework", "Flask", ["flask"]),
        new("framework", "Express", ["express.js", "express js", "express"]),
        new("framework", "NestJS", ["nestjs", "nest js"]),
        new("framework", "React", ["react"]),
        new("framework", "Angular", ["angular"]),
        new("framework", "Vue", ["vue", "vue.js", "vue js"]),
        new("framework", "Node.js", ["node.js", "node js", "nodejs"]),

        new("system_design", "System Design", ["system design"]),
        new("system_design", "Microservices", ["microservices", "microservice"]),
        new("system_design", "Distributed Systems", ["distributed systems", "distributed system"]),
        new("system_design", "Event-Driven Architecture", ["event-driven", "event driven", "event bus", "message-driven"]),
        new("system_design", "Modular Monolith", ["modular monolith"]),
        new("system_design", "Domain-Driven Design", ["domain-driven design", "domain driven design", "ddd"]),
        new("system_design", "Clean Architecture", ["clean architecture"]),
        new("system_design", "Scalability", ["scalability", "scalable systems", "high scale"]),
        new("system_design", "Observability", ["observability", "monitoring", "tracing"]),
        new("system_design", "API Design", ["api design", "rest api", "restful api", "api architecture"]),

        new("database", "PostgreSQL", ["postgresql", "postgres"]),
        new("database", "SQL Server", ["sql server", "mssql"]),
        new("database", "MySQL", ["mysql"]),
        new("database", "MongoDB", ["mongodb", "mongo db"]),
        new("database", "Redis", ["redis"]),
        new("database", "Oracle", ["oracle"]),
        new("database", "Elasticsearch", ["elasticsearch", "elastic search"]),
        new("database", "Database Design", ["database design", "schema design", "data modeling", "data model"]),

        new("delivery_methodology", "Agile", ["agile"]),
        new("delivery_methodology", "Scrum", ["scrum"]),
        new("delivery_methodology", "Kanban", ["kanban"]),
        new("delivery_methodology", "Sprint Planning", ["sprint planning"]),
        new("delivery_methodology", "Backlog Refinement", ["backlog refinement", "grooming"]),
        new("delivery_methodology", "Retrospective", ["retrospective", "retro"]),
        new("delivery_methodology", "User Stories", ["user stories", "user story"])
    ];

    private readonly ILlmClient _llmClient = llmClient;
    private readonly HiringWorkflowSettings _settings = settings ?? HiringWorkflowSettings.CreateDefault();

    public async Task<IReadOnlyList<InterviewQuestion>> BuildQuestionsAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string interviewLanguage = "EN",
        CancellationToken cancellationToken = default)
    {
        var fallback = BuildFallbackQuestionPack(request, technicalInterviewRole, interviewLanguage);
        try
        {
            var generated = await BuildFullInterviewPackAsync(request, technicalInterviewRole, interviewLanguage, cancellationToken);
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
        var interviewLanguage = HiringConversationLanguageResolver.ResolveInitialLanguage(request.ProjectBrief, request.JobDescription, request.CandidateCv, TryReadNotes(sessionNotesPath));
        var fallback = BuildFallbackQuestion(request, technicalInterviewRole, speaker, questionNumber, sessionNotesPath, interviewLanguage);
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
        string interviewLanguage = "EN",
        CancellationToken cancellationToken = default)
    {
        var fallback = BuildFallbackInterviewerReply(request, speaker, candidateQuestion, sessionNotesPath, interviewLanguage);
        try
        {
            var generated = await BuildInterviewerReplyWithLlmAsync(request, technicalInterviewRole, speaker, candidateQuestion, sessionNotesPath, interviewLanguage, cancellationToken);
            return string.IsNullOrWhiteSpace(generated) ? fallback : generated;
        }
        catch
        {
            return fallback;
        }
    }

    private IReadOnlyList<InterviewQuestion> BuildFallbackQuestionPack(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string interviewLanguage)
    {
        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
        var skillAlignment = BuildSkillAlignment(request.ProjectBrief, request.JobDescription, request.CandidateCv);
        var topicKeywords = skillAlignment.GetPrimaryTopics(5);
        var topicText = topicKeywords.Length == 0 ? "your most relevant technical work" : string.Join(", ", topicKeywords);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectBrief"] = request.ProjectBrief,
            ["TopicText"] = topicText,
            ["RequirementFocusText"] = skillAlignment.ToRequirementFocusText(),
            ["AlignedSkillText"] = skillAlignment.ToOverlapText(),
            ["TechnicalInterviewRole"] = technicalInterviewRole,
            ["SeniorityLevel"] = seniorityLevel.ToLowerInvariant()
        };

        var questions = new List<InterviewQuestion>();
        questions.AddRange(_settings.GeneralQuestions.Select(template => BuildQuestion(template, replacements, topicKeywords, interviewLanguage)));

        var technicalTemplates = _settings.TechnicalQuestions
            .Where(template => string.Equals(template.AppliesToTechnicalRole, technicalInterviewRole, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        for (var index = 1; index <= _settings.TechnicalQuestionCount; index++)
        {
            var technicalTemplate = technicalTemplates.Length == 0
                ? _settings.TechnicalQuestions.FirstOrDefault()
                : technicalTemplates[(index - 1) % technicalTemplates.Length];

            if (technicalTemplate is not null)
                questions.Add(BuildQuestion(technicalTemplate, replacements, topicKeywords, interviewLanguage));
        }

        questions.AddRange(_settings.BusinessQuestions.Select(template => BuildQuestion(template, replacements, topicKeywords, interviewLanguage)));
        questions.AddRange(_settings.ClosingQuestions.Select(template => BuildQuestion(template, replacements, topicKeywords, interviewLanguage)));
        return questions;
    }

    private InterviewQuestion BuildFallbackQuestion(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string speaker,
        int questionNumber,
        string sessionNotesPath,
        string interviewLanguage)
    {
        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
        var notesContent = TryReadNotes(sessionNotesPath);
        var skillAlignment = BuildSkillAlignment(request.ProjectBrief, request.JobDescription, request.CandidateCv);
        var topicKeywords = ExtractKeywords(notesContent).Take(5).ToArray();
        if (topicKeywords.Length == 0)
            topicKeywords = skillAlignment.GetPrimaryTopics(5);

        var topicText = topicKeywords.Length == 0 ? "your most relevant technical work" : string.Join(", ", topicKeywords);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectBrief"] = request.ProjectBrief,
            ["TopicText"] = topicText,
            ["RequirementFocusText"] = skillAlignment.ToRequirementFocusText(),
            ["AlignedSkillText"] = skillAlignment.ToOverlapText(),
            ["TechnicalInterviewRole"] = technicalInterviewRole,
            ["SeniorityLevel"] = seniorityLevel.ToLowerInvariant()
        };

        var template = ResolveTemplate(speaker, technicalInterviewRole, questionNumber);
        return BuildQuestion(template, replacements, topicKeywords, interviewLanguage);
    }

    private async Task<IReadOnlyList<InterviewQuestion>> BuildFullInterviewPackAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string interviewLanguage,
        CancellationToken cancellationToken)
    {
        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(request.TargetSeniority, request.ProjectBrief, request.JobDescription, request.CandidateCv, _settings.Scoring);
        var seniorityProfile = HiringSeniorityResolver.ResolveProfile(seniorityLevel, _settings.Scoring);
        var skillAlignment = BuildSkillAlignment(request.ProjectBrief, request.JobDescription, request.CandidateCv);
        var systemPrompt = $$"""
                You are an expert interviewer designing a staged interview for a software candidate.
                Read the project brief, job description, candidate CV, and selected technical interviewer role.
                Generate a one-shot interview question bank for this hiring session.

                Primary goal:
                - Evaluate the candidate as a whole professional, not as a keyword checklist and not as a trivia exam.
                - Derive the project stack focus fresh from this specific project brief and this specific JD. Do not assume a default stack from other jobs, past sessions, or generic market patterns.
                - Ground the technical questions in the overlap between this session's project-critical stack areas and the candidate's demonstrated skills.
                - Prioritise questions that reveal reasoning, ownership, trade-off judgment, collaboration, communication, and learning.
                - Make the interview heavily evidence-based around the project's tech stack, the candidate's actual experience, and role-relevant delivery work.
                - Do not spend the question bank on broad situational prompts.
                - Tune the difficulty and expected scope to the target seniority level.
                - If an important project requirement is not clearly proven in the CV, ask about the closest adjacent experience and how the candidate would transfer that experience into this project.

                Seniority target:
                - Level: {{seniorityLevel}}
                - Summary: {{seniorityProfile.Summary}}
                - Expected behaviours: {{string.Join(", ", seniorityProfile.ExpectedBehaviors)}}
                - Scoring guidance: {{seniorityProfile.ScoreGuidance}}

                Language rules:
                - The interview language is locked to {{interviewLanguage}}.
                - Write every candidate-facing question, follow-up, and hint keyword in that locked language only.
                - Do not mix English and Vietnamese in the same interview bank.

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
                    1. exactly {{_settings.GeneralQuestionCount}} PM opening questions
                    2. exactly {{_settings.TechnicalQuestionCount}} {{technicalInterviewRole}} technical questions
                    3. exactly 1 HR closing question
                - First infer 3-5 current JD stack priorities from the project brief and JD only. Use the CV only after that, to judge where the candidate has strong evidence, partial evidence, or a gap.
                - PM questions must be tailored to the project brief, JD, CV, and seniority target, and should quickly anchor the interview in the candidate's real stack experience that best matches the project requirements.
                - Most of the interview must focus on the project's stack, the candidate's actual implementations, debugging work, deployment or release decisions, quality strategy, and measurable outcomes.
                - The {{technicalInterviewRole}} questions must be grounded in the candidate's actual experience and should test how the candidate thinks through real work decisions, not isolated tool recall.
                - Prefer asking about the strongest overlap areas first. Use missing-but-important requirements only for a smaller number of transferability questions.
                - At least 70% of technical questions should target the current JD stack priorities directly.
                - Do not let unrelated CV strengths dominate the bank if they are not materially important in this JD.
                - Each technical question should clearly connect to one of these: overlapping stack area, project-critical requirement, adjacent experience that can transfer to the project.
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

                Locked interview language:
                {interviewLanguage}

                Current JD stack priorities inferred from this specific hiring request:
                {skillAlignment.ToRequirementFocusText()}

                Strongest overlap between project requirements and candidate background:
                {skillAlignment.ToOverlapText()}

                Project-critical requirements with weaker or unclear CV evidence:
                {skillAlignment.ToRequirementGapText()}

                Candidate strengths adjacent to the project stack:
                {skillAlignment.ToCandidateAdjacencyText()}

                Job description:
                {request.JobDescription}

                Candidate CV:
                {request.CandidateCv}
                """;

        var response = await _llmClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        var generated = TryParseGeneratedQuestions(response);
        return HasExpectedStructure(generated, technicalInterviewRole, _settings.GeneralQuestionCount, _settings.TechnicalQuestionCount)
            && MatchesInterviewLanguage(generated, interviewLanguage)
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
                                - Keep the interview weighted toward project-stack depth and the candidate's real shipped work.
                                - Situational or hypothetical exploration may account for at most {{_settings.Scoring.SituationQuestionScoreCap:P0}} of evaluative weight.
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
                                - For {{technicalInterviewRole}} turns, prefer concrete questions about architecture, APIs, data, testing strategy, debugging, delivery, production risks, and verification.
                                - For BA turns, keep the question concise and scenario-focused because situational assessment is intentionally limited.
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
                string interviewLanguage,
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
                        - The interview language is locked to {{interviewLanguage}}. Reply only in that language.
                        - If the candidate asks to switch language, acknowledge the request briefly but keep the interview in the locked language.
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

    private static bool HasExpectedStructure(IReadOnlyList<InterviewQuestion>? questions, string technicalInterviewRole, int generalQuestionCount, int technicalQuestionCount)
    {
        if (questions is null || questions.Count < 3)
            return false;

        var technicalCount = questions.Count(question => string.Equals(question.Speaker, technicalInterviewRole, StringComparison.OrdinalIgnoreCase));
        var hasHr = questions.Any(question => string.Equals(question.Speaker, "HR", StringComparison.OrdinalIgnoreCase));
        var pmCount = questions.Count(question => string.Equals(question.Speaker, "PM", StringComparison.OrdinalIgnoreCase));

        return pmCount >= generalQuestionCount && technicalCount >= technicalQuestionCount && hasHr;
    }

    private static bool MatchesInterviewLanguage(IReadOnlyList<InterviewQuestion>? questions, string interviewLanguage)
    {
        if (questions is null || questions.Count == 0)
            return false;

        return string.Equals(interviewLanguage, "VI", StringComparison.OrdinalIgnoreCase)
            ? questions.All(question => HiringConversationLanguageResolver.ResolveInitialLanguage(question.Text, question.FollowUpText ?? string.Empty) == "VI")
            : questions.All(question => HiringConversationLanguageResolver.ResolveInitialLanguage(question.Text, question.FollowUpText ?? string.Empty) != "VI");
    }

    private InterviewQuestion BuildQuestion(
        InterviewQuestionTemplate template,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyCollection<string> topicKeywords,
        string interviewLanguage)
    {
        var hintKeywords = template.HintKeywords.Concat(topicKeywords).Distinct(StringComparer.OrdinalIgnoreCase).Take(_settings.HintKeywordCount).ToArray();
        var textTemplate = SelectTextTemplate(template, interviewLanguage);
        var followUpTemplate = SelectFollowUpTemplate(template, interviewLanguage);
        var text = Render(textTemplate, replacements);
        var followUp = string.IsNullOrWhiteSpace(followUpTemplate) ? null : Render(followUpTemplate, replacements);

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
            var technicalTemplates = _settings.TechnicalQuestions
                .Where(template => string.Equals(template.AppliesToTechnicalRole, technicalInterviewRole, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var index = Math.Clamp(questionNumber - 1, 0, Math.Max(0, technicalTemplates.Length - 1));
            return technicalTemplates.ElementAtOrDefault(index)
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
        string sessionNotesPath,
        string interviewLanguage)
    {
        var lower = candidateQuestion.Trim().ToLowerInvariant();

        if (string.Equals(interviewLanguage, "VI", StringComparison.OrdinalIgnoreCase))
        {
            if (lower.Contains("english", StringComparison.Ordinal))
                return "Mình đã chốt phỏng vấn bằng tiếng Việt ở đầu buổi, nên mình sẽ tiếp tục bằng tiếng Việt nhé. Bạn cứ trả lời ngắn gọn theo ví dụ thực tế gần nhất là được.";

            return "Mình đang làm rõ ý của câu hỏi để bạn có thể trả lời bằng kinh nghiệm thực tế của mình. Bạn cứ trả lời ngắn gọn theo ví dụ gần nhất nhé.";
        }

        if (lower.Contains("tiếng việt", StringComparison.Ordinal) || lower.Contains("tieng viet", StringComparison.Ordinal))
            return "We agreed to keep the interview in English at the start, so I will continue in English. A short, concrete example is enough.";

        return "I am clarifying the intent of the question so you can answer from your real experience. A short, concrete example is enough.";
    }

    private static string SelectTextTemplate(InterviewQuestionTemplate template, string interviewLanguage)
    {
        return string.Equals(interviewLanguage, "VI", StringComparison.OrdinalIgnoreCase)
            ? template.VietnameseTextTemplate ?? template.TextTemplate
            : template.TextTemplate;
    }

    private static string? SelectFollowUpTemplate(InterviewQuestionTemplate template, string interviewLanguage)
    {
        return string.Equals(interviewLanguage, "VI", StringComparison.OrdinalIgnoreCase)
            ? template.VietnameseFollowUpTemplate ?? template.FollowUpTemplate
            : template.FollowUpTemplate;
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
            .Select(token => NormalizeKeyword(token.Trim()))
            .Where(token => token.Length >= 3 && !stopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static SkillAlignment BuildSkillAlignment(string projectBrief, string jobDescription, string candidateCv)
    {
        var requirementFocus = ExtractRankedSkills($"{projectBrief} {jobDescription}", 10);
        if (requirementFocus.Length == 0)
            requirementFocus = ExtractRankedKeywords($"{projectBrief} {jobDescription}", 10);

        var candidateEvidence = ExtractRankedSkills(candidateCv, 12);
        if (candidateEvidence.Length == 0)
            candidateEvidence = ExtractRankedKeywords(candidateCv, 12);

        var candidateEvidenceSet = candidateEvidence.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requirementFocusSet = requirementFocus.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var overlap = requirementFocus
            .Where(keyword => candidateEvidenceSet.Contains(keyword))
            .Take(8)
            .ToArray();
        var requirementGaps = requirementFocus
            .Where(keyword => !candidateEvidenceSet.Contains(keyword))
            .Take(6)
            .ToArray();
        var candidateAdjacencies = candidateEvidence
            .Where(keyword => !requirementFocusSet.Contains(keyword))
            .Take(6)
            .ToArray();

        return new SkillAlignment(requirementFocus, overlap, requirementGaps, candidateAdjacencies);
    }

    private static string[] ExtractRankedSkills(string text, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var normalizedText = NormalizeSearchText(text);
        var matches = SkillCatalog
            .Select(signal => new
            {
                signal.Category,
                signal.CanonicalName,
                FirstIndex = GetFirstMatchIndex(normalizedText, signal.MatchTerms)
            })
            .Where(match => match.FirstIndex >= 0)
            .OrderBy(match => Array.IndexOf(SkillCategoryPriority, match.Category))
            .ThenBy(match => match.FirstIndex)
            .Select(match => match.CanonicalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();

        return matches;
    }

    private static string[] ExtractRankedKeywords(string text, int maxCount)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "with", "from", "that", "this", "your", "have", "has", "will", "need",
            "candidate", "project", "role", "years", "team", "experience", "work", "using", "used"
        };

        return text
            .Split([' ', '\n', '\r', '\t', ',', '.', ':', ';', '(', ')', '/', '\\', '-', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select((token, index) => new { Token = NormalizeKeyword(token.Trim()), Index = index })
            .Where(item => item.Token.Length >= 3 && !stopWords.Contains(item.Token))
            .GroupBy(item => item.Token, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Min(item => item.Index))
            .Select(group => group.Key)
            .Take(maxCount)
            .ToArray();
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

    private static string NormalizeSearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '#' or '+' or '.' ? character : ' ');
        }

        return builder.ToString();
    }

    private static int GetFirstMatchIndex(string normalizedText, IReadOnlyList<string> matchTerms)
    {
        var firstIndex = -1;
        foreach (var term in matchTerms)
        {
            var normalizedTerm = NormalizeSearchText(term).Trim();
            if (string.IsNullOrWhiteSpace(normalizedTerm))
                continue;

            var index = normalizedText.IndexOf(normalizedTerm, StringComparison.Ordinal);
            if (index >= 0 && (firstIndex < 0 || index < firstIndex))
                firstIndex = index;
        }

        return firstIndex;
    }

    private static string TryReadNotes(string sessionNotesPath)
    {
        if (string.IsNullOrWhiteSpace(sessionNotesPath) || !File.Exists(sessionNotesPath))
            return string.Empty;

        return File.ReadAllText(sessionNotesPath);
    }

    private sealed record GeneratedQuestionPayload(List<GeneratedQuestionItem> Questions);

    private sealed record GeneratedQuestionItem(string Speaker, string Text, string? FollowUpText, List<string> HintKeywords);

    private sealed record SkillSignal(string Category, string CanonicalName, IReadOnlyList<string> MatchTerms);

    private sealed record SkillAlignment(
        IReadOnlyList<string> RequirementFocus,
        IReadOnlyList<string> Overlap,
        IReadOnlyList<string> RequirementGaps,
        IReadOnlyList<string> CandidateAdjacencies)
    {
        public string[] GetPrimaryTopics(int count)
        {
            return RequirementFocus
                .Concat(Overlap)
                .Concat(RequirementGaps)
                .Concat(CandidateAdjacencies)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(count)
                .ToArray();
        }

        public string ToRequirementFocusText()
        {
            return RequirementFocus.Count == 0
                ? "No clear stack priority could be extracted, so infer the main implementation areas from the current JD wording."
                : string.Join(", ", RequirementFocus);
        }

        public string ToOverlapText()
        {
            return Overlap.Count == 0
                ? "No exact overlap is obvious, so focus on the closest transferable technical experience."
                : string.Join(", ", Overlap);
        }

        public string ToRequirementGapText()
        {
            return RequirementGaps.Count == 0
                ? "No major uncovered requirement detected from the extracted materials."
                : string.Join(", ", RequirementGaps);
        }

        public string ToCandidateAdjacencyText()
        {
            return CandidateAdjacencies.Count == 0
                ? "No major adjacent strengths detected beyond the direct overlap."
                : string.Join(", ", CandidateAdjacencies);
        }
    }
}