using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;
using System.Text.Json;

namespace PMAgent.Infrastructure;

/// <summary>
/// Runs the agent loop: Think → Action → Input → Output.
/// Each iteration produces an <see cref="AgentStep"/>.
/// When a step has <c>IsFinal = true</c> the loop stops and the
/// accumulated result is returned.
///
/// When an <see cref="ILlmClient"/> is provided, the Think step uses the LLM
/// following the ReAct (Reasoning + Acting) pattern to decide the next tool.
/// If the LLM is unavailable or returns an unparseable response, the executor
/// falls back to the built-in rule-based sequence automatically.
/// </summary>
public sealed class AgentExecutor : IAgentExecutor
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;
    private readonly ILlmClient? _llm;
    private readonly ILogger<AgentExecutor> _logger;

    public AgentExecutor(
        IEnumerable<IAgentTool> tools,
        ILogger<AgentExecutor> logger,
        ILlmClient? llm = null)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _llm = llm;
    }

    public async Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[AgentExecutor] Starting run. Goal: {Goal}", request.Goal);
        var totalSw = Stopwatch.StartNew();

        var memory = new InMemoryAgentMemory();
        if (!string.IsNullOrWhiteSpace(request.Context))
            memory.Record("CONTEXT", request.Context);

        var steps = new List<AgentStep>();

        for (var iteration = 0; iteration < request.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runningContext = memory.BuildContext();

            // ── THINK ────────────────────────────────────────────────────────
            var (thought, action, actionInput, isFinal) =
                await ThinkAsync(request.Goal, runningContext, steps, cancellationToken);

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

            // Record the tool output into memory so the next Think can reason
            // over all accumulated findings without manual string concatenation.
            memory.Record(action.ToUpperInvariant(), actionOutput);

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

    // ── THINK (LLM-driven with rule-based fallback) ───────────────────────────
    private async Task<(string thought, string action, string input, bool isFinal)>
        ThinkAsync(string goal, string context, List<AgentStep> steps, CancellationToken ct)
    {
        if (_llm is not null)
        {
            try
            {
                var systemPrompt = BuildThinkSystemPrompt();
                var userPrompt = BuildThinkUserPrompt(goal, context, steps);
                var raw = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);
                return ParseThinkResponse(raw, context);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "[AgentExecutor] LLM Think step failed. Falling back to rule-based sequence.");
            }
        }

        return ThinkRuleBased(goal, context, steps);
    }

    private string BuildThinkSystemPrompt()
    {
        var toolList = string.Join("\n", _tools.Values.Select(t => $"- {t.Name}: {t.Description}"));

        return """
            You are a ReAct (Reasoning + Acting) planning agent.
            Your task is to decide the next action toward the stated goal.

            ## Available Tools
            TOOL_LIST_PLACEHOLDER

            ## Instructions
            1. Review the goal and all previous steps.
            2. Choose the most appropriate next tool, OR decide you have gathered enough information to finalize.
            3. Do NOT repeat a tool that was already used in a previous step.
            4. Respond ONLY with valid JSON — no markdown, no code fences, no extra text.

            ## Response Format
            To invoke a tool:
            {"thought": "Your reasoning here", "action": "tool_name", "actionInput": "Input for the tool"}

            To finalize (when all necessary tools have been used or you have enough information):
            {"thought": "Your reasoning here", "isFinal": true}
            """.Replace("TOOL_LIST_PLACEHOLDER", toolList);
    }

    private static string BuildThinkUserPrompt(string goal, string context, List<AgentStep> steps)
    {
        var history = steps.Count == 0
            ? "None yet."
            : string.Join("\n", steps.Select((s, i) =>
                $"Step {i + 1}: [{s.Action}] {s.Thought}\n  Output: {s.ActionOutput[..Math.Min(200, s.ActionOutput.Length)]}"));

        return $"""
            Goal: {goal}

            Previous steps:
            {history}

            Current context:
            {context}
            """;
    }

    private static (string thought, string action, string input, bool isFinal)
        ParseThinkResponse(string raw, string context)
    {
        var json = raw.Trim();

        // Strip markdown code fences when the model wraps the JSON in them.
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('\n') + 1;
            var end = json.LastIndexOf("```");
            if (end > start)
                json = json[start..end].Trim();
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var thought = root.TryGetProperty("thought", out var tp) ? tp.GetString() ?? "" : "";

        if (root.TryGetProperty("isFinal", out var fp) && fp.GetBoolean())
            return (thought, "finalize", context, true);

        var action = root.TryGetProperty("action", out var ap) ? ap.GetString() ?? "" : "";
        var actionInput = root.TryGetProperty("actionInput", out var aip)
            ? aip.GetString() ?? context
            : context;

        return (thought, action, actionInput, false);
    }

    // ── RULE-BASED FALLBACK ───────────────────────────────────────────────────
    private static (string thought, string action, string input, bool isFinal)
        ThinkRuleBased(string goal, string context, List<AgentStep> steps)
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
