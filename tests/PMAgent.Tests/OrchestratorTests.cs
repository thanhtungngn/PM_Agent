using Microsoft.Extensions.Logging.Abstractions;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using PMAgent.Infrastructure;
using PMAgent.Infrastructure.Agents;

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
        return new(
            [
                new ProductOwnerAgent(llm, NullLogger<ProductOwnerAgent>.Instance),
                new ProjectManagerAgent(llm, NullLogger<ProjectManagerAgent>.Instance),
                new BusinessAnalystAgent(llm, NullLogger<BusinessAnalystAgent>.Instance),
                new DeveloperAgent(llm, NullLogger<DeveloperAgent>.Instance),
                new TesterAgent(llm, NullLogger<TesterAgent>.Instance)
            ],
            NullLogger<OrchestratorAgent>.Instance);
    }

    [Fact]
    public async Task RunAsync_RunsAllFiveAgents()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Build a project management SaaS tool");

        var result = await orchestrator.RunAsync(request);

        Assert.Equal(5, result.AgentOutputs.Count);
    }

    [Fact]
    public async Task RunAsync_OutputContainsAllRoles()
    {
        var orchestrator = BuildOrchestrator();
        var request = new OrchestrationRequest("Build an e-commerce platform");

        var result = await orchestrator.RunAsync(request);

        var roles = result.AgentOutputs.Select(r => r.Role).ToList();
        Assert.Contains("PO", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PM", roles, StringComparer.OrdinalIgnoreCase);
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
        var request = new OrchestrationRequest("Build a CRM system");

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
}
