using Microsoft.Extensions.Logging.Abstractions;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using PMAgent.Infrastructure.Services;

namespace PMAgent.Tests;

/// <summary>
/// Fast harness coverage for the staged hiring workflow.
/// These tests exercise the real workflow service and file outputs without a real LLM.
/// </summary>
[Trait("Category", "Harness")]
public sealed class HiringHarnessTests : IDisposable
{
    private readonly string _notesRootPath = Path.Combine(Path.GetTempPath(), $"pmagent-hiring-harness-{Guid.NewGuid():N}");

    private sealed class FakeInterviewScoringLlmClient : ILlmClient
    {
        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
                        if (systemPrompt.Contains("HR screening evaluator", StringComparison.OrdinalIgnoreCase))
                        {
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
                                return Task.FromResult("Được, mình sẽ trao đổi bằng tiếng Việt từ bây giờ. Bạn cứ tiếp tục nhé.");

                            return Task.FromResult("Sure. Let me clarify the intent of the current question so you can answer from your actual experience.");
                        }

                        if (systemPrompt.Contains("expert interviewer", StringComparison.OrdinalIgnoreCase))
                        {
                                return Task.FromResult("""
                                        {
                                            "questions": [
                                                {
                                                    "speaker": "PM",
                                                    "text": "Please introduce yourself through the parts of your experience that are most relevant to this project stack and role.",
                                                    "followUpText": "Which parts of that experience map most directly to the project stack we just described?",
                                                    "hintKeywords": ["project stack", "owned systems", "role fit"]
                                                },
                                                {
                                                    "speaker": "DEV",
                                                    "text": "Walk us through a technical decision you made around API design, PostgreSQL performance, and deployment safety.",
                                                    "followUpText": "What would you change now with more production hindsight?",
                                                    "hintKeywords": ["trade-offs", "architecture", "validation"]
                                                },
                                                {
                                                    "speaker": "DEV",
                                                    "text": "Tell us about a production debugging or deployment issue you handled on a backend service and how you narrowed it down.",
                                                    "followUpText": null,
                                                    "hintKeywords": ["debugging", "signals", "evidence"]
                                                },
                                                {
                                                    "speaker": "DEV",
                                                    "text": "Given this project, which part of the stack would you inspect first in week one and what technical risks would you surface early?",
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
    public async Task FullPanelFlow_TransitionsStagesAndWritesArtifacts()
    {
        var service = BuildService();

        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Hire a backend engineer for a platform project",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, cloud deployment, and API design experience.",
            "Built ASP.NET Core APIs with PostgreSQL, Docker, Azure deployment pipelines, and production support.",
            TechnicalInterviewRole: "DEV",
            AutoApproveInterviewSchedule: false));

        Assert.Equal("awaiting_screening_approval", started.Stage);
        Assert.True(Directory.Exists(started.CandidateFolder));

        var scheduled = await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));
        Assert.Equal("awaiting_interview_approval", scheduled.Stage);

        var interview = await service.ApproveInterviewScheduleAsync(started.SessionId, new HiringApprovalRequest(true));
        Assert.Equal("interview_active", interview.Stage);
        Assert.Equal("PM", interview.CurrentSpeaker);

        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I am a backend engineer with six years of experience building .NET APIs for SaaS products and owning production releases."));
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "The most relevant part is owning ASP.NET Core APIs, PostgreSQL schema work, and Docker-based releases for production services."));
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I chose a modular monolith with explicit API contracts, caching for hot reads, and tracing to validate performance trade-offs."));
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "With hindsight I would add observability earlier and define service-level objectives from the beginning."));
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "A tricky production issue involved a slow query path, and I narrowed it down with traces, query plans, and release diff checks before shipping the fix."));
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "In week one I would inspect API boundaries, data access hotspots, and deployment safety because those are the fastest places to surface technical risk."));
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I would clarify stakeholder impact, estimate trade-offs, and document the decision before changing scope."));
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I would like to know how success is measured in the first 90 days and how releases are handled."));

        Assert.Equal("completed", interview.Stage);
        Assert.True(interview.InterviewScore > 0);
        Assert.True(File.Exists(interview.NotesDocumentPath));
        Assert.True(File.Exists(Path.Combine(started.CandidateFolder, "jd-keywords.md")));
        Assert.True(File.Exists(Path.Combine(started.CandidateFolder, "cv-keywords.md")));
        Assert.True(File.Exists(Path.Combine(started.CandidateFolder, "interview-qa.md")));
    }

    [Fact]
    public async Task FollowUpFlow_ExposesPendingFollowUpBeforeAdvancing()
    {
        var service = BuildService();
        var started = await StartInterviewAsync(service);

        var questionWithFollowUp = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I am a backend engineer with six years of experience building .NET APIs."));

        Assert.Equal("PM", questionWithFollowUp.CurrentSpeaker);
        Assert.False(string.IsNullOrWhiteSpace(questionWithFollowUp.CurrentPrompt));
        Assert.Contains("project stack", questionWithFollowUp.CurrentPrompt, StringComparison.OrdinalIgnoreCase);

        var afterPrimaryAnswer = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I would first own API boundaries and deployment safety for the first release."));

        Assert.Equal("interview_active", afterPrimaryAnswer.Stage);
        Assert.Equal("DEV", afterPrimaryAnswer.CurrentSpeaker);
    }

    [Fact]
    public async Task HintFlow_LeavesQuestionActive_AndWritesHintToQaLog()
    {
        var service = BuildService();
        var started = await StartInterviewAsync(service);
        var beforeHint = await service.GetAsync(started.SessionId);

        Assert.NotNull(beforeHint);
        var originalSpeaker = beforeHint!.CurrentSpeaker;

        var hinted = await service.RequestHintAsync(started.SessionId);

        Assert.Equal("interview_active", hinted.Stage);
        Assert.Equal(originalSpeaker, hinted.CurrentSpeaker);
        Assert.Contains("prompts that might help", hinted.CurrentPrompt, StringComparison.OrdinalIgnoreCase);

        var qaContent = await File.ReadAllTextAsync(Path.Combine(hinted.CandidateFolder, "interview-qa.md"));
        Assert.Contains("[HINT]", qaContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnswerFlow_WritesInterviewerFeedbackToQaLog()
    {
        var service = BuildService();
        var started = await StartInterviewAsync(service);

        var result = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I owned .NET APIs, PostgreSQL tuning, and Docker deployments in production."));

        var qaContent = await File.ReadAllTextAsync(Path.Combine(result.CandidateFolder, "interview-qa.md"));
        Assert.Contains("[FEEDBACK]", qaContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VietnameseInterview_UsesVietnameseRuntimeMessages()
    {
        var service = BuildService();
        var started = await service.StartAsync(new HiringSessionStartRequest(
            "Tuyển backend engineer cho nền tảng nội bộ",
            "Cần C#, ASP.NET Core, PostgreSQL và Docker.",
            "Đã làm API .NET, PostgreSQL và triển khai Docker trong production.",
            TechnicalInterviewRole: "DEV",
            AutoApproveInterviewSchedule: false));

        var scheduled = await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));
        var interview = await service.ApproveInterviewScheduleAsync(scheduled.SessionId, new HiringApprovalRequest(true));

        Assert.True(interview.Transcript.Count(turn => turn.Message.Contains("Chào bạn", StringComparison.OrdinalIgnoreCase)) >= 2);
    }

    [Fact]
    public async Task ClarificationFlow_RepliesWithoutAdvancingQuestion()
    {
        var service = BuildService();
        var started = await StartInterviewAsync(service);
        var beforeClarification = await service.GetAsync(started.SessionId);

        Assert.NotNull(beforeClarification);
        var originalSpeaker = beforeClarification!.CurrentSpeaker;

        var clarified = await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("Could you clarify what you mean by contribute to first?"));

        Assert.Equal("interview_active", clarified.Stage);
        Assert.Equal(originalSpeaker, clarified.CurrentSpeaker);
        Assert.Contains("clarify", clarified.CurrentPrompt, StringComparison.OrdinalIgnoreCase);

        var afterClarification = await service.GetAsync(started.SessionId);
        Assert.NotNull(afterClarification);
        Assert.Equal(originalSpeaker, afterClarification!.CurrentSpeaker);
    }

    [Fact]
    public async Task VietnameseClarificationFlow_RepliesWithoutAdvancingQuestion()
    {
        var service = BuildService();
        var started = await StartInterviewAsync(service, new HiringSessionStartRequest(
            "Tuyển backend engineer cho nền tảng nội bộ",
            "Cần C#, ASP.NET Core, PostgreSQL và Docker.",
            "Đã làm API .NET, PostgreSQL và triển khai Docker trong production.",
            TechnicalInterviewRole: "DEV"));

        var clarified = await service.SubmitCandidateResponseAsync(
            started.SessionId,
            new HiringCandidateResponseRequest("bạn có thể nói tiếng Việt được không?"));

        Assert.Equal("interview_active", clarified.Stage);
        Assert.Equal("PM", clarified.CurrentSpeaker);
        Assert.Contains("tiếng Việt", clarified.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EarlyStopFlow_StopsInterviewAndWritesNotes()
    {
        var service = BuildService();
        var started = await StartInterviewAsync(service);

        await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I do not know. I am not sure what to say yet."));

        var stopped = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I do not know. I have no experience with that."));

        Assert.Equal("completed", stopped.Stage);
        Assert.True(stopped.InterviewScore < 40);
        Assert.True(File.Exists(stopped.NotesDocumentPath));
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

    private static async Task<HiringSessionResult> StartInterviewAsync(InMemoryHiringWorkflowService service)
        => await StartInterviewAsync(service, new HiringSessionStartRequest(
            "Hire a backend engineer for a SaaS billing platform",
            "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
            "Built C# ASP.NET Core APIs, tuned PostgreSQL, deployed Docker workloads, and designed REST APIs.",
            TechnicalInterviewRole: "DEV"));

    private static async Task<HiringSessionResult> StartInterviewAsync(InMemoryHiringWorkflowService service, HiringSessionStartRequest request)
    {
        var started = await service.StartAsync(request);

        return await service.ApproveScreeningAsync(started.SessionId, new HiringApprovalRequest(true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_notesRootPath))
            Directory.Delete(_notesRootPath, true);
    }
}