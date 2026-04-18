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
            var lower = userPrompt.ToLowerInvariant();
            if (lower.Contains("i do not know") || lower.Contains("no experience"))
            {
                return Task.FromResult("""
                    {"score": 22, "shouldStop": true, "rationale": "Concerns outweigh strengths. The candidate repeatedly shows uncertainty on critical topics."}
                    """);
            }

            return Task.FromResult("""
                {"score": 84, "shouldStop": false, "rationale": "The candidate demonstrates relevant delivery evidence and answers with enough technical depth to continue."}
                """);
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
        Assert.True(result.ScreeningFitScore >= 70);
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
        // Q1 has no follow-up (Simple), so moves to Q2 PM
        Assert.Equal("PM", interview.CurrentSpeaker);

        // Q2: PM project — answer triggers follow-up
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I would start with the API and data model boundaries, then work with the team on PostgreSQL schema design and deployment safety for the first release."));
        // Q2 has a follow-up; follow-up is now active
        Assert.Equal("PM", interview.CurrentSpeaker);

        // Q2 follow-up answer — moves to DEV
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "In the first week I would focus on quick wins and understanding the team delivery rhythm, then align on a 4-week milestone."));
        Assert.Equal("DEV", interview.CurrentSpeaker);

        // Q3: DEV technical — answer triggers follow-up
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "One key decision was choosing a modular monolith with clear API contracts, caching hot reads, and measuring performance with tracing before scaling out."));
        Assert.Equal("DEV", interview.CurrentSpeaker);

        // Q3 follow-up answer — moves to BA
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "With hindsight I would instrument observability earlier and define SLOs from day one."));
        Assert.Equal("BA", interview.CurrentSpeaker);

        // Q4: BA scenario — answer triggers follow-up
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "If a stakeholder changes requirements late, I clarify impact, estimate the cost, align on priorities, and document the decision before changing scope."));
        Assert.Equal("BA", interview.CurrentSpeaker);

        // Q4 follow-up answer — moves to HR closing
        interview = await service.SubmitCandidateResponseAsync(started.SessionId, new HiringCandidateResponseRequest(
            "I would write a change-log entry, update the RACI, and send a summary to the team."));
        Assert.Equal("HR", interview.CurrentSpeaker);

        // Q5: HR closing Q&A — completes the interview
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
        Assert.Contains("keyword", hintResult.CurrentPrompt, StringComparison.OrdinalIgnoreCase);
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
        // The question should still be about the project, not have moved on
        Assert.Equal("PM", clarified.CurrentSpeaker);
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
        new(
            new LlmInterviewScoringAgent(new FakeInterviewScoringLlmClient(), NullLogger<LlmInterviewScoringAgent>.Instance),
            NullLogger<InMemoryHiringWorkflowService>.Instance,
            _notesRootPath);

    public void Dispose()
    {
        if (Directory.Exists(_notesRootPath))
            Directory.Delete(_notesRootPath, true);
    }
}