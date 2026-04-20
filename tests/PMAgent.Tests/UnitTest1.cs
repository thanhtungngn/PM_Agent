using Microsoft.Extensions.Logging.Abstractions;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using PMAgent.Infrastructure;
using PMAgent.Infrastructure.Services;
using PMAgent.Infrastructure.Tools;

namespace PMAgent.Tests;

/// <summary>
/// Deterministic LLM stub — echoes the user prompt back as output so tests can
/// assert that goals and context flow through the agent pipeline unchanged.
/// JSON-based responses are intentionally not returned here so the ReAct Think
/// step falls back to the rule-based sequence, keeping tests fully deterministic.
/// </summary>
file sealed class FakeEchoLlmClient : ILlmClient
{
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(userPrompt);
}

public class RuleBasedAgentPlannerTests
{
    [Fact]
    public async Task BuildPlanAsync_ReturnsActionsAndRisks()
    {
        var planner = new RuleBasedAgentPlanner();

        var request = new PlanningRequest(
            "PM Agent",
            "Build MVP for project managers",
            ["2 month deadline"],
            ["Alice", "Bob"]);

        var result = await planner.BuildPlanAsync(request);

        Assert.NotEmpty(result.NextActions);
        Assert.NotEmpty(result.Risks);
        Assert.Contains(result.NextActions, x => x.Contains("MVP", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Risks, x => x.Contains("Constraints", StringComparison.OrdinalIgnoreCase));
    }
}

public class AgentExecutorTests
{
    private static AgentExecutor BuildExecutor()
    {
        var fakeLlm = new FakeEchoLlmClient();
        return new(
            [new ScopeAnalysisTool(fakeLlm), new RiskAssessmentTool(fakeLlm), new ActionPlannerTool(fakeLlm)],
            NullLogger<AgentExecutor>.Instance);
    }

    [Fact]
    public async Task RunAsync_ProducesIsFinalStep()
    {
        var executor = BuildExecutor();
        var request = new AgentRunRequest("Build MVP", "Initial context");

        var result = await executor.RunAsync(request);

        Assert.True(result.Steps.Any(s => s.IsFinal),
            "At least one step must have IsFinal = true.");
    }

    [Fact]
    public async Task RunAsync_LastStepIsAlwaysFinal()
    {
        var executor = BuildExecutor();
        var request = new AgentRunRequest("Ship project dashboard", "");

        var result = await executor.RunAsync(request);

        Assert.True(result.Steps.Last().IsFinal,
            "The last step in completed run must have IsFinal = true.");
    }

    [Fact]
    public async Task RunAsync_AllThreeToolsAreInvoked()
    {
        var executor = BuildExecutor();
        var request = new AgentRunRequest("Deliver feature X", "");

        var result = await executor.RunAsync(request);

        var actions = result.Steps.Select(s => s.Action).ToList();
        Assert.Contains("scope_analysis", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk_assessment", actions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("action_planner", actions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_EachStepHasNonEmptyThoughtAndOutput()
    {
        var executor = BuildExecutor();
        var request = new AgentRunRequest("Launch product", "");

        var result = await executor.RunAsync(request);

        foreach (var step in result.Steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Thought),
                $"Step '{step.Action}' must have a non-empty Thought.");
            Assert.False(string.IsNullOrWhiteSpace(step.ActionOutput),
                $"Step '{step.Action}' must have a non-empty ActionOutput.");
        }
    }

    [Fact]
    public async Task RunAsync_FinalAnswerContainsGoal()
    {
        var goal = "Reduce time-to-market by 30%";
        var executor = BuildExecutor();
        var request = new AgentRunRequest(goal, "");

        var result = await executor.RunAsync(request);

        Assert.Contains(goal, result.FinalAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_RespectsMaxIterationsGuard()
    {
        // With MaxIterations = 1 the loop must stop after a single iteration.
        var executor = BuildExecutor();
        var request = new AgentRunRequest("Quick check", "", MaxIterations: 1);

        var result = await executor.RunAsync(request);

        Assert.Single(result.Steps);
    }
}
