using Microsoft.Extensions.Logging.Abstractions;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using PMAgent.Infrastructure;
using PMAgent.Infrastructure.Agents;
using PMAgent.Infrastructure.Services;

namespace PMAgent.Tests;

/// <summary>
/// Deterministic LLM stub — returns a single string containing all keywords
/// the orchestrator tests need to verify, regardless of the prompt received.
/// </summary>
file sealed class FakeLlmClient : ILlmClient
{
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            $$"""
            ## Output
            {{userPrompt}}
            Product Vision, Goals, User Stories, Acceptance Criteria
            ### Milestones
            ### Resource Plan
            ### Risk Register
            ### Hiring Plan
            ### CV Keywords
            ### JD Fit Assessment
            ### Technical Interview Questions
            ### QA Scenario Questions
            ### Evaluation Scorecard
            ### Functional Requirements
            ### Use Cases
            ### Architecture
            ### Technology Stack
            ### Test Plan
            ### Quality Gates
            """);
}

public class OrchestratorAgentTests
{
    private static OrchestratorAgent BuildOrchestrator()
    {
        var llm = new FakeLlmClient();
        var routingPolicy = new RuleBasedAgentRoutingPolicy();
        return new(
            [
                new ProductOwnerAgent(llm, NullLogger<ProductOwnerAgent>.Instance),
                new ProjectManagerAgent(llm, NullLogger<ProjectManagerAgent>.Instance),
                new HrAgent(llm, NullLogger<HrAgent>.Instance),
                new BusinessAnalystAgent(llm, NullLogger<BusinessAnalystAgent>.Instance),
                new DeveloperAgent(llm, NullLogger<DeveloperAgent>.Instance),
                new TesterAgent(llm, NullLogger<TesterAgent>.Instance)
            ],
            routingPolicy,
            NullLogger<OrchestratorAgent>.Instance);
    }

    [Fact]
    public async Task RunAsync_RunsAllSixAgents()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Build an enterprise multi-tenant project management SaaS tool with external integration");

        var result = await orchestrator.RunAsync(request);

        Assert.Equal(6, result.AgentOutputs.Count);
    }

    [Fact]
    public async Task RunAsync_OutputContainsAllRoles()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Build an enterprise e-commerce platform with compliance and migration requirements");

        var result = await orchestrator.RunAsync(request);

        var roles = result.AgentOutputs.Select(r => r.Role).ToList();
        Assert.Contains("PO", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PM", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("HR", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("BA", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DEV", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TEST", roles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_AllAgentsReportSuccess()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Deliver a mobile banking app");

        var result = await orchestrator.RunAsync(request);

        Assert.All(result.AgentOutputs, r =>
            Assert.True(r.Success, $"Agent '{r.Role}' must report Success = true."));
    }

    [Fact]
    public async Task RunAsync_AllAgentsExposeRoutingMetadata()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Design a reporting module");

        var result = await orchestrator.RunAsync(request);

        Assert.All(result.AgentOutputs, r =>
        {
            Assert.Equal("continue", r.Decision);
            Assert.InRange(r.Confidence, 0.0, 1.0);
            Assert.NotNull(r.Issues);
            Assert.Equal("continue", r.NextAction);
        });
    }

    [Fact]
    public async Task RunAsync_SummaryIsNotEmpty()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Launch an analytics dashboard");

        var result = await orchestrator.RunAsync(request);

        Assert.False(string.IsNullOrWhiteSpace(result.Summary),
            "Summary must not be empty.");
    }

    [Fact]
    public async Task RunAsync_PO_OutputContainsBrief()
    {
        var brief = "Build a task-tracking tool for remote teams";
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest(brief);

        var result = await orchestrator.RunAsync(request);

        var poOutput = result.AgentOutputs.Single(r => r.Role == "PO").Output;
        Assert.Contains(brief, poOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_PM_OutputContainsMilestones()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Create a milestone roadmap for CRM system launch");

        var result = await orchestrator.RunAsync(request);

        var pmOutput = result.AgentOutputs.Single(r => r.Role == "PM").Output;
        Assert.Contains("Milestone", pmOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_BA_OutputContainsRequirements()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Build an HR onboarding portal");

        var result = await orchestrator.RunAsync(request);

        var baOutput = result.AgentOutputs.Single(r => r.Role == "BA").Output;
        Assert.Contains("Requirement", baOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_HR_OutputContainsHiringPlan()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Plan recruitment for a mobile banking product team");

        var result = await orchestrator.RunAsync(request);

        var hrOutput = result.AgentOutputs.Single(r => r.Role == "HR").Output;
        Assert.Contains("Hiring Plan", hrOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_DEV_OutputContainsArchitecture()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Build a real-time chat application");

        var result = await orchestrator.RunAsync(request);

        var devOutput = result.AgentOutputs.Single(r => r.Role == "DEV").Output;
        Assert.Contains("Architecture", devOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_TEST_OutputContainsTestPlan()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Build a document management system");

        var result = await orchestrator.RunAsync(request);

        var testOutput = result.AgentOutputs.Single(r => r.Role == "TEST").Output;
        Assert.Contains("Test Plan", testOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_EmptyBrief_ThrowsArgumentException()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            orchestrator.RunAsync(request));
    }

    [Fact]
    public async Task RunAsync_PlanningIntent_SkipsDevAndTest()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Create a roadmap and milestone plan for the next release");

        var result = await orchestrator.RunAsync(request);

        var roles = result.AgentOutputs.Select(r => r.Role).ToList();
        Assert.Contains("PO", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PM", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("BA", roles, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("HR", roles, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEV", roles, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("TEST", roles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_HiringWorkflow_RunsPmHrBaByDefault()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest(
            "Create a hiring and staffing plan for the project team",
            Workflow: "hiring",
            JobDescription: "Senior backend engineer with .NET, APIs, PostgreSQL, and cloud deployment experience.",
            CandidateCv: "5 years in C#, ASP.NET Core, PostgreSQL, Docker, Azure DevOps, and microservices.");

        var result = await orchestrator.RunAsync(request);

        var roles = result.AgentOutputs.Select(r => r.Role).ToList();
        Assert.Equal(["PM", "HR", "BA"], roles);
    }

    [Fact]
    public async Task RunAsync_HiringWorkflow_WithTechnicalInterviewRoles_RunsPmHrBaDevTest()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest(
            "Hire application engineers and QA for the next release",
            Workflow: "hiring",
            JobDescription: "Need a backend developer and QA engineer with API, automation, and CI/CD experience.",
            CandidateCv: "Candidate has C#, REST API, xUnit, Playwright, SQL, and CI pipeline experience.",
            TechnicalInterviewRoles: ["DEV", "TEST"]);

        var result = await orchestrator.RunAsync(request);

        var roles = result.AgentOutputs.Select(r => r.Role).ToList();
        Assert.Equal(["PM", "HR", "BA", "DEV", "TEST"], roles);
    }

    [Fact]
    public async Task RunAsync_HiringWorkflow_SeedsJobDescriptionAndCvIntoAgentContext()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest(
            "Screen a backend engineer candidate",
            Workflow: "hiring",
            JobDescription: "Looking for .NET and PostgreSQL experience.",
            CandidateCv: "Candidate worked on ASP.NET Core APIs and PostgreSQL scaling.");

        var result = await orchestrator.RunAsync(request);

        var hrOutput = result.AgentOutputs.Single(r => r.Role == "HR").Output;
        Assert.Contains("Looking for .NET and PostgreSQL experience.", hrOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Candidate worked on ASP.NET Core APIs and PostgreSQL scaling.", hrOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_HiringWorkflow_DEVInterviewPackContainsTechnicalInterviewQuestions()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest(
            "Prepare a backend technical interview",
            Workflow: "hiring",
            JobDescription: "Senior backend developer for distributed APIs.",
            CandidateCv: "Candidate has .NET, distributed systems, Kafka, and SQL.",
            TechnicalInterviewRoles: ["DEV"]);

        var result = await orchestrator.RunAsync(request);

        var devOutput = result.AgentOutputs.Single(r => r.Role == "DEV").Output;
        Assert.Contains("Technical Interview Questions", devOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Evaluation Scorecard", devOutput, StringComparison.OrdinalIgnoreCase);
    }
}
