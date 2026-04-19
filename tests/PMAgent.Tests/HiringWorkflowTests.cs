using Microsoft.Extensions.Logging.Abstractions;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using PMAgent.Infrastructure.Services;

namespace PMAgent.Tests;

public sealed class HiringWorkflowTests : IDisposable
{
    private readonly string _notesRootPath = Path.Combine(Path.GetTempPath(), $"pmagent-hiring-tests-{Guid.NewGuid():N}");

    private sealed class FakeInterviewScoringLlmClient : ILlmClient
    {
        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
                        if (systemPrompt.Contains("HR screening evaluator", StringComparison.OrdinalIgnoreCase))
                        {
                                if (userPrompt.Contains("manual testing", StringComparison.OrdinalIgnoreCase)
                                        || userPrompt.Contains("customer support", StringComparison.OrdinalIgnoreCase))
                                {
                                        return Task.FromResult("""
                                                {"score": 28, "shouldAdvance": false, "summary": "The CV does not align strongly with the backend role.", "strengths": ["general software exposure"], "gaps": ["c#", "asp.net core", "postgresql", "docker"]}
                                                """);
                                }

                                return Task.FromResult("""
                                        {"score": 86, "shouldAdvance": true, "summary": "The CV aligns well with the backend role and shows relevant delivery evidence.", "strengths": ["asp.net core", "postgresql", "docker"], "gaps": ["distributed systems"]}
                                        """);
                        }

                        if (systemPrompt.Contains("continuing an in-progress interview", StringComparison.OrdinalIgnoreCase))
                        {
                            return Task.FromResult(BuildSingleQuestion(userPrompt));
                        }

                        if (systemPrompt.Contains("active interviewer in an ongoing hiring interview", StringComparison.OrdinalIgnoreCase))
                        {
                            if (userPrompt.Contains("tiếng Việt", StringComparison.OrdinalIgnoreCase) || userPrompt.Contains("tieng viet", StringComparison.OrdinalIgnoreCase))
                                return Task.FromResult("Được, từ giờ tôi sẽ trao đổi bằng tiếng Việt. Bạn cứ tiếp tục với ví dụ thực tế gần nhất nhé.");

                            return Task.FromResult("Sure. I am clarifying the intent of the current question so you can answer with a concrete example from your recent work.");
                        }

                        if (systemPrompt.Contains("expert interviewer", StringComparison.OrdinalIgnoreCase))
                        {
                                var technicalRole = userPrompt.Contains("Technical interviewer role:\nTEST", StringComparison.OrdinalIgnoreCase)
                                    || userPrompt.Contains("Technical interviewer role:\r\nTEST", StringComparison.OrdinalIgnoreCase)
                                    || userPrompt.Contains("Technical interviewer role:\n            TEST", StringComparison.OrdinalIgnoreCase)
                                    ? "TEST"
                                    : "DEV";

                                var technicalQuestion = technicalRole == "TEST"
                                    ? "Walk us through how you would design a QA strategy for API, regression, and release confidence."
                                    : "Walk us through a technical decision you made around API design, PostgreSQL performance, and deployment safety.";
                                var technicalFollowUp = technicalRole == "TEST"
                                    ? "What metrics would you track to prove release quality is improving?"
                                    : "What would you change now with more production hindsight?";
                                var technicalQuestion2 = technicalRole == "TEST"
                                    ? "Tell us about a release or regression issue you investigated on a real system and how you narrowed it down."
                                    : "Tell us about a production debugging or deployment issue you handled on a backend service and how you narrowed it down.";
                                var technicalQuestion3 = technicalRole == "TEST"
                                    ? "For this project, which quality risks in the stack would you assess first and how would you make those risks visible early?"
                                    : "Given this project, which part of the stack would you inspect first in week one and what technical risks would you surface early?";

                                return Task.FromResult($$"""
                                        {
                                            "questions": [
                                                {
                                                    "speaker": "PM",
                                                    "text": "Please introduce yourself through the parts of your experience that are most relevant to this project stack and role.",
                                                    "followUpText": "Which parts of that experience map most directly to the project stack we just described?",
                                                    "hintKeywords": ["project stack", "owned systems", "role fit"]
                                                },
                                                {
                                                    "speaker": "{{technicalRole}}",
                                                    "text": "{{technicalQuestion}}",
                                                    "followUpText": "{{technicalFollowUp}}",
                                                    "hintKeywords": ["trade-offs", "architecture", "validation"]
                                                },
                                                {
                                                    "speaker": "{{technicalRole}}",
                                                    "text": "{{technicalQuestion2}}",
                                                    "followUpText": null,
                                                    "hintKeywords": ["debugging", "signals", "evidence"]
                                                },
                                                {
                                                    "speaker": "{{technicalRole}}",
                                                    "text": "{{technicalQuestion3}}",
                                                    "followUpText": null,
                                                    "hintKeywords": ["risk", "stack priorities", "week one"]
                                                },
                                                {
                                                    "speaker": "BA",
                                                    "text": "If a stakeholder changes requirements late in the cycle, how would you clarify impact and align priorities?",
                                                    "followUpText": null,
                                                    "hintKeywords": ["impact", "stakeholders", "change log"]
                                                },
                                                {
                                                    "speaker": "HR",
                                                    "text": "What questions do you have for the team before we close?",
                                                    "followUpText": null,
                                                    "hintKeywords": ["team", "success", "expectations"]
                                                }
                                            ]
                                        }
                                        """);
                        }

            var lower = userPrompt.ToLowerInvariant();
            if (lower.Contains("i do not know") || lower.Contains("no experience"))
            {
                return Task.FromResult("""
                            {"score": 22, "shouldStop": true, "rationale": "Concerns outweigh strengths. The candidate repeatedly shows uncertainty on critical topics.", "dimensions": [{"name": "communication", "score": 35, "summary": "Uncertain answers."}, {"name": "problem_solving", "score": 24, "summary": "Limited structure in the approach."}, {"name": "technical_judgment", "score": 20, "summary": "Limited technical substance."}, {"name": "ownership", "score": 18, "summary": "Weak ownership evidence."}, {"name": "collaboration", "score": 25, "summary": "Limited delivery alignment."}]}
                    """);
            }

            return Task.FromResult("""
                        {"score": 84, "shouldStop": false, "rationale": "The candidate demonstrates relevant delivery evidence and answers with enough depth and judgment to continue.", "dimensions": [{"name": "communication", "score": 82, "summary": "Clear and structured."}, {"name": "problem_solving", "score": 84, "summary": "Breaks down problems clearly."}, {"name": "technical_judgment", "score": 88, "summary": "Strong role alignment."}, {"name": "ownership", "score": 80, "summary": "Shows execution ownership."}, {"name": "collaboration", "score": 78, "summary": "Shows delivery awareness."}]}
                """);
        }

        private static string BuildSingleQuestion(string userPrompt)
        {
            if (userPrompt.Contains("Requested speaker:\nPM", StringComparison.OrdinalIgnoreCase)
                && userPrompt.Contains("Question number for this speaker:\n1", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    { "speaker": "PM", "text": "Please introduce yourself through the parts of your experience that are most relevant to this project stack and role.", "followUpText": "Which parts of that experience map most directly to the project stack we just described?", "hintKeywords": ["project stack", "owned systems", "role fit"] }
                    """;
            }

            if (userPrompt.Contains("Requested speaker:\nTEST", StringComparison.OrdinalIgnoreCase))
            {
                if (userPrompt.Contains("Question number for this speaker:\n1", StringComparison.OrdinalIgnoreCase))
                {
                    return """
                        { "speaker": "TEST", "text": "Walk us through how you would design a QA strategy for API, regression, and release confidence.", "followUpText": "What metrics would you track to prove release quality is improving?", "hintKeywords": ["trade-offs", "architecture", "validation"] }
                        """;
                }

                if (userPrompt.Contains("Question number for this speaker:\n2", StringComparison.OrdinalIgnoreCase))
                {
                    return """
                        { "speaker": "TEST", "text": "Tell us about a release or regression issue you investigated on a real system and how you narrowed it down.", "followUpText": null, "hintKeywords": ["debugging", "signals", "evidence"] }
                        """;
                }

                return """
                    { "speaker": "TEST", "text": "For this project, which quality risks in the stack would you assess first and how would you make those risks visible early?", "followUpText": null, "hintKeywords": ["risk", "stack priorities", "week one"] }
                    """;
            }

            if (userPrompt.Contains("Requested speaker:\nDEV", StringComparison.OrdinalIgnoreCase))
            {
                if (userPrompt.Contains("Question number for this speaker:\n1", StringComparison.OrdinalIgnoreCase))
                {
                    return """
                        { "speaker": "DEV", "text": "Walk us through a technical decision you made around API design, PostgreSQL performance, and deployment safety.", "followUpText": "What would you change now with more production hindsight?", "hintKeywords": ["trade-offs", "architecture", "validation"] }
                        """;
                }

                if (userPrompt.Contains("Question number for this speaker:\n2", StringComparison.OrdinalIgnoreCase))
                {
                    return """
                        { "speaker": "DEV", "text": "Tell us about a production debugging or deployment issue you handled on a backend service and how you narrowed it down.", "followUpText": null, "hintKeywords": ["debugging", "signals", "evidence"] }
                        """;
                }

                return """
                    { "speaker": "DEV", "text": "Given this project, which part of the stack would you inspect first in week one and what technical risks would you surface early?", "followUpText": null, "hintKeywords": ["risk", "stack priorities", "week one"] }
                    """;
            }

            if (userPrompt.Contains("Requested speaker:\nBA", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    { "speaker": "BA", "text": "If a stakeholder changes requirements late in the cycle, how would you clarify impact and align priorities?", "followUpText": null, "hintKeywords": ["impact", "stakeholders", "change log"] }
                    """;
            }

            return """
                { "speaker": "HR", "text": "What questions do you have for the team before we close?", "followUpText": null, "hintKeywords": ["team", "success", "expectations"] }
                """;
        }
    }

    [Fact]
    public async Task StartAsync_FitAboveThreshold_WaitsForHrApproval()
    {
        var service = BuildService();

        var result = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a SaaS billing platform",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads, and designed REST APIs."));

        Assert.Equal("awaiting_screening_approval", result.Stage);
        Assert.True(result.RequiresUserApproval);
        Assert.Equal("screening_forward", result.ApprovalType);
        Assert.Equal("HR", result.CurrentSpeaker);
        Assert.True(result.ScreeningFitScore >= 40);
        Assert.Equal("MID", result.SeniorityLevel);
    }

    [Fact]
    public async Task StartAsync_ExplicitSeniority_IsReturnedInSession()
    {
        var service = BuildService();

        var result = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a SaaS billing platform",
            "Need a senior .NET engineer who can mentor and make architecture decisions.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads, and designed REST APIs.",
            TargetSeniority: "SENIOR"));

        Assert.Equal("SENIOR", result.SeniorityLevel);
    }

    [Fact]
    public async Task ApproveScreening_AutoApproveDisabled_WaitsForPmApproval()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a QA engineer",
            "Need API testing, Playwright, regression strategy, and CI experience.",
            "Led Playwright automation, API testing, regression planning, and CI execution.",
            TechnicalInterviewRole: "TEST",
            AutoApproveInterviewSchedule: false));

        var result = await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));

        Assert.Equal("awaiting_interview_approval", result.Stage);
        Assert.True(result.RequiresUserApproval);
        Assert.Equal("interview_schedule", result.ApprovalType);
        Assert.Equal("PM", result.CurrentSpeaker);
    }

    [Fact]
    public async Task HiringInterview_ProgressesThroughPanelAndWritesNotes()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a platform project",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, cloud deployment, and API design experience.",
            "Built ASP.NET Core APIs with PostgreSQL, Docker, Azure deployment pipelines, and production support.",
            TechnicalInterviewRole: "DEV"));

        var interview = await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));
        Assert.Equal("interview_active", interview.Stage);
        Assert.Equal("PM", interview.CurrentSpeaker);

        // Q1: PM intro — triggers PM follow-up
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "Hello, I am a backend engineer with six years of experience building .NET APIs for SaaS products and owning production releases."));
        Assert.Equal("PM", interview.CurrentSpeaker);

        // PM follow-up answer — moves to first technical round
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "The most relevant part is building ASP.NET Core APIs, tuning PostgreSQL queries, and owning Docker-based releases for billing-related flows."));
        Assert.Equal("DEV", interview.CurrentSpeaker);

        // DEV technical round 1 — answer triggers technical follow-up
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "One key decision was choosing a modular monolith with clear API contracts, caching hot reads, and measuring performance with tracing before scaling out."));
        Assert.Equal("DEV", interview.CurrentSpeaker);

        // DEV follow-up answer — moves to second technical round
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "With hindsight I would instrument observability earlier and define SLOs from day one."));
        Assert.Equal("DEV", interview.CurrentSpeaker);

        // DEV technical round 2 — moves to technical round 3
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "A difficult production issue involved a slow query path under peak load, and I narrowed it down with traces, query plans, and release diff checks before shipping the fix."));
        Assert.Equal("DEV", interview.CurrentSpeaker);

        // DEV technical round 3 — moves to BA
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "In week one I would inspect API boundaries, data access hotspots, and deployment safety first, because those are the fastest places to surface architectural and operational risk."));
        Assert.Equal("BA", interview.CurrentSpeaker);

        // BA scenario — moves to HR closing
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "If a stakeholder changes requirements late, I clarify impact, estimate the cost, align on priorities, and document the decision before changing scope."));
        Assert.Equal("HR", interview.CurrentSpeaker);

        // HR closing Q&A — completes the interview
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "My main question is how success is measured in the first 90 days and how the team handles production incidents during release week."));

        Assert.Equal("completed", interview.Stage);
        Assert.True(interview.InterviewScore > 0);
        Assert.False(string.IsNullOrWhiteSpace(interview.NotesDocumentPath));
        Assert.True(File.Exists(interview.NotesDocumentPath));
    }

    [Fact]
    public async Task StartAsync_CreatesPerCandidateFolderWithKeywordFiles()
    {
        var service = BuildService();

        var result = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for billing platform",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads, and designed REST APIs."));

        Assert.False(string.IsNullOrWhiteSpace(result.CandidateFolder));
        Assert.True(Directory.Exists(result.CandidateFolder));
        Assert.True(File.Exists(Path.Combine(result.CandidateFolder, "jd-keywords.md")));
        Assert.True(File.Exists(Path.Combine(result.CandidateFolder, "cv-keywords.md")));
        Assert.True(File.Exists(Path.Combine(result.CandidateFolder, "interview-qa.md")));
    }

    [Fact]
    public async Task RequestHint_WhileInterviewActive_ReturnsHintAndSameQuestion()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a SaaS billing platform",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads.",
            TechnicalInterviewRole: "DEV"));

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));

        var hintResult = await service.RequestHintAsync(started.SessionId);

        Assert.Equal("interview_active", hintResult.Stage);
        Assert.Contains("prompts that might help", hintResult.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NaturalLanguageHintRequest_RoutesToHintFlow()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Tuyển backend engineer cho nền tảng thanh toán",
            "Cần C#, ASP.NET Core, PostgreSQL, Docker và kinh nghiệm thiết kế API.",
            "Đã xây dựng API bằng C# ASP.NET Core, tối ưu PostgreSQL và triển khai Docker.",
            TechnicalInterviewRole: "DEV"));

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));

        var hintResult = await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("bạn có thể cho tôi 1 gợi ý được không?"));

        Assert.Equal("interview_active", hintResult.Stage);
        Assert.Contains("gợi ý", hintResult.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CandidateClarificationQuestion_ReceivesInterviewerReply_SameQuestion()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a SaaS billing platform",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads."));

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));

        // Candidate asks a clarification question
        var clarified = await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("Could you clarify what you mean by 'contribute to first'?"));

        Assert.Equal("interview_active", clarified.Stage);
        Assert.Equal("PM", clarified.CurrentSpeaker);
        Assert.Contains("clarifying", clarified.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(clarified.Transcript, turn => string.Equals(turn.Speaker, "EVAL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CandidateProcessQuestion_IsHandledAsInterviewRequest_NotAsAnswer()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a SaaS billing platform",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads."));

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));

        var result = await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("Before I answer, could you tell me how the backend team usually works with PM?"));

        Assert.Equal("interview_active", result.Stage);
        Assert.Equal("PM", result.CurrentSpeaker);
        Assert.Contains("clarifying", result.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Transcript, turn => string.Equals(turn.Speaker, "EVAL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RepeatedCandidateRequests_TriggersFocusReminderBackToActiveQuestion()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a SaaS billing platform",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads."));

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));

        await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("Could you clarify what you mean by 'relevant to this role'?"));

        var redirected = await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("Before I answer, can I also ask how success is measured in this role?"));

        Assert.Equal("interview_active", redirected.Stage);
        Assert.Equal("PM", redirected.CurrentSpeaker);
        Assert.Contains("come back to the main question", redirected.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Please introduce yourself", redirected.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CandidateLanguageSwitch_ChangesRuntimeReplyToVietnamese()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Tuyển backend engineer cho một sản phẩm SaaS",
            "Cần C#, ASP.NET Core, PostgreSQL, Docker và kinh nghiệm thiết kế API.",
            "Đã xây dựng API bằng C# ASP.NET Core, tối ưu PostgreSQL và triển khai Docker."));

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));

        var clarified = await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("bạn có thể nói tiếng Việt được không?"));

        Assert.Equal("interview_active", clarified.Stage);
        Assert.Equal("PM", clarified.CurrentSpeaker);
        Assert.Contains("tiếng Việt", clarified.CurrentPrompt, StringComparison.OrdinalIgnoreCase);

        var notes = await File.ReadAllTextAsync(clarified.NotesDocumentPath);
        Assert.Contains("ConversationLanguage: VI", notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HiringInterview_WritesLiveQaFileWithQuestionsAndAnswers()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a platform project",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, cloud deployment, and API design experience.",
            "Built ASP.NET Core APIs with PostgreSQL, Docker, Azure deployment pipelines, and production support.",
            TechnicalInterviewRole: "DEV"));

        Assert.False(string.IsNullOrWhiteSpace(started.CandidateFolder));
        var qaPath = Path.Combine(started.CandidateFolder, "interview-qa.md");

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));
        await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I am a backend engineer with six years of experience building .NET APIs."));

        var qaContent = await File.ReadAllTextAsync(qaPath);
        Assert.Contains("PM", qaContent);
        Assert.Contains("CANDIDATE", qaContent);
        Assert.Contains("[FEEDBACK]", qaContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CandidateResponses_LowScore_StopsInterviewEarly()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer",
            "Need C#, API design, PostgreSQL, and cloud deployment experience.",
            "Worked with C#, APIs, and PostgreSQL in production."));

        await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));
        await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest("I am not sure. I do not know what to say yet."));
        var result = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest("I do not know. I have no experience with that."));

        Assert.Equal("completed", result.Stage);
        Assert.True(result.InterviewScore < 40);
    }

    [Fact]
    public async Task StartAsync_FitBelowThreshold_RejectsImmediately()
    {
        var service = BuildService();

        var result = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, distributed systems, and API design experience.",
            "Strong experience in manual testing, Excel reporting, and customer support workflows."));

        Assert.Equal("rejected", result.Stage);
        Assert.False(string.IsNullOrWhiteSpace(result.NotesDocumentPath));
        Assert.True(File.Exists(result.NotesDocumentPath));
    }

    private InMemoryHiringWorkflowService BuildService() =>
        BuildServiceCore();

    private InMemoryHiringWorkflowService BuildServiceCore()
    {
        var settings = HiringWorkflowSettings.CreateDefault();
        var llm = new FakeInterviewScoringLlmClient();
        return new InMemoryHiringWorkflowService(
            new LlmHiringFitScoringAgent(llm, settings, NullLogger<LlmHiringFitScoringAgent>.Instance),
            new LlmInterviewScoringAgent(llm, settings, NullLogger<LlmInterviewScoringAgent>.Instance),
            new ConfigurableInterviewQuestionProvider(llm, settings),
            settings,
            NullLogger<InMemoryHiringWorkflowService>.Instance,
            _notesRootPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_notesRootPath))
            Directory.Delete(_notesRootPath, true);
    }
}