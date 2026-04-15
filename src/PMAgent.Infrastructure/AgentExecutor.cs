using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;

namespace PMAgent.Infrastructure;

/// <summary>
/// Runs the agent loop: Think → Action → Input → Output.
/// Each iteration produces an <see cref="AgentStep"/>.
/// When a step has <c>IsFinal = true</c> the loop stops and the
/// accumulated result is returned.
/// </summary>
public sealed class AgentExecutor : IAgentExecutor
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;
    private readonly ILogger<AgentExecutor> _logger;

    public AgentExecutor(IEnumerable<IAgentTool> tools, ILogger<AgentExecutor> logger)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[AgentExecutor] Starting run. Goal: {Goal}", request.Goal);
        var totalSw = Stopwatch.StartNew();

        var steps = new List<AgentStep>();
        var runningContext = request.Context;

        for (var iteration = 0; iteration < request.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ── THINK ────────────────────────────────────────────────────────
            var (thought, action, actionInput, isFinal) =
                Think(request.Goal, runningContext, steps);

            _logger.LogDebug(
                "[AgentExecutor] Iteration {Iteration}: action={Action}, isFinal={IsFinal}. Thought: {Thought}",
                iteration, action, isFinal, thought);

            // ── ACTION + INPUT → OUTPUT ───────────────────────────────────────
            string actionOutput;
            var sw = Stopwatch.StartNew();

            if (isFinal)
            {
                actionOutput = BuildFinalAnswer(request.Goal, steps);
            }
            else if (_tools.TryGetValue(action, out var tool))
            {
                actionOutput = await tool.ExecuteAsync(actionInput, cancellationToken);
            }
            else
            {
                _logger.LogWarning("[AgentExecutor] Tool '{Action}' not found. Skipping.", action);
                actionOutput = $"Tool '{action}' not found. Skipping.";
            }

            sw.Stop();
            _logger.LogDebug(
                "[AgentExecutor] Action '{Action}' finished in {ElapsedMs} ms. Output length: {Chars} chars.",
                action, sw.ElapsedMilliseconds, actionOutput.Length);

            var step = new AgentStep(thought, action, actionInput, actionOutput, isFinal);
            steps.Add(step);

            // Append this step's output to the shared context so the next
            // iteration's Think can reason over accumulated knowledge.
            runningContext = $"{runningContext}\n[{action.ToUpperInvariant()}]: {actionOutput}";

            // ── LOOP GUARD ────────────────────────────────────────────────────
            if (isFinal)
                break;
        }

        totalSw.Stop();
        _logger.LogInformation(
            "[AgentExecutor] Run complete. {StepCount} steps, total {ElapsedMs} ms.",
            steps.Count, totalSw.ElapsedMilliseconds);

        var finalAnswer = steps.LastOrDefault(s => s.IsFinal)?.ActionOutput
            ?? "Agent reached the maximum iterations without producing a final answer.";

        return new AgentRunResult(finalAnswer, steps);
    }

    // ── THINK ─────────────────────────────────────────────────────────────────
    // Decides the next (thought, action, input, isFinal) tuple by inspecting
    // which tools have already been used in previous steps.
    private static (string thought, string action, string input, bool isFinal)
        Think(string goal, string context, List<AgentStep> steps)
    {
        var usedActions = new HashSet<string>(
            steps.Select(s => s.Action),
            StringComparer.OrdinalIgnoreCase);

        if (!usedActions.Contains("scope_analysis"))
        {
            return (
                $"I need to understand the scope of the goal: '{goal}'. Let me analyse it first.",
                "scope_analysis",
                goal,
                false
            );
        }

        if (!usedActions.Contains("risk_assessment"))
        {
            return (
                "Scope is clear. Now I need to identify potential risks before planning.",
                "risk_assessment",
                context,
                false
            );
        }

        if (!usedActions.Contains("action_planner"))
        {
            return (
                "I have the scope and the risks. Now I can create a concrete action plan.",
                "action_planner",
                context,
                false
            );
        }

        // All tools have run — produce the final answer.
        return (
            "I have gathered sufficient information. Producing the final answer now.",
            "finalize",
            context,
            true
        );
    }

    private static string BuildFinalAnswer(string goal, List<AgentStep> completedSteps)
    {
        var lines = completedSteps.Select(s =>
            $"[{s.Action.ToUpperInvariant()}]\n  Thought : {s.Thought}\n  Output  : {s.ActionOutput}");

        return $"Agent completed planning for goal: '{goal}'.\n\n" +
               string.Join("\n\n", lines);
    }
}
