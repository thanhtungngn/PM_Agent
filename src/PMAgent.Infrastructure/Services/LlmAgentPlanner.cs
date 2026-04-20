using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Text.Json;

namespace PMAgent.Infrastructure.Services;

/// <summary>
/// LLM-backed implementation of <see cref="IAgentPlanner"/> that produces
/// context-aware project plans by reasoning over the actual project name,
/// goal, team, and constraints — instead of returning static templates.
/// Falls back to a deterministic response when the LLM is unavailable or
/// returns an unparseable response.
/// </summary>
public sealed class LlmAgentPlanner : IAgentPlanner
{
    private readonly ILlmClient _llm;

    private const string SystemPrompt =
        """
        You are an expert project manager. Given a project name, goal, team members, and constraints,
        produce a structured project plan as JSON with exactly these three fields:
        {
          "summary": "A short, actionable summary of the plan (1-2 sentences)",
          "nextActions": ["Action 1", "Action 2", ...],
          "risks": ["Risk 1 — mitigation hint", "Risk 2 — mitigation hint", ...]
        }

        Requirements:
        - nextActions: 4-8 specific, measurable actions with realistic targets
        - risks: 3-5 concrete risks; each string must include a brief mitigation hint
        - summary: focused on the goal and the key delivery approach
        Respond ONLY with valid JSON — no markdown, no code fences, no extra text.
        """;

    public LlmAgentPlanner(ILlmClient llm)
    {
        _llm = llm;
    }

    public async Task<PlanningResponse> BuildPlanAsync(
        PlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        var userPrompt = BuildUserPrompt(request);

        try
        {
            var raw = await _llm.CompleteAsync(SystemPrompt, userPrompt, cancellationToken);
            return ParseResponse(raw);
        }
        catch
        {
            return BuildFallbackResponse(request);
        }
    }

    private static string BuildUserPrompt(PlanningRequest request)
    {
        var teamSection = request.TeamMembers.Count > 0
            ? $"Team: {string.Join(", ", request.TeamMembers)}"
            : "Team: Not specified";

        var constraintsSection = request.Constraints.Count > 0
            ? $"Constraints: {string.Join("; ", request.Constraints)}"
            : "Constraints: None specified";

        return $"""
            Project: {request.ProjectName}
            Goal: {request.Goal}
            {teamSection}
            {constraintsSection}
            """;
    }

    private static PlanningResponse ParseResponse(string raw)
    {
        var json = raw.Trim();

        // Strip markdown code fences when the model wraps the JSON.
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('\n') + 1;
            var end = json.LastIndexOf("```");
            if (end > start)
                json = json[start..end].Trim();
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";

        var nextActions = root.TryGetProperty("nextActions", out var na)
            ? na.EnumerateArray()
                .Select(x => x.GetString() ?? "")
                .Where(x => x.Length > 0)
                .ToList()
            : (List<string>)[];

        var risks = root.TryGetProperty("risks", out var r)
            ? r.EnumerateArray()
                .Select(x => x.GetString() ?? "")
                .Where(x => x.Length > 0)
                .ToList()
            : (List<string>)[];

        return new PlanningResponse(summary, nextActions, risks);
    }

    private static PlanningResponse BuildFallbackResponse(PlanningRequest request)
    {
        var actions = new List<string>
        {
            $"Clarify acceptance criteria for '{request.ProjectName}'.",
            "Break work into weekly milestones with measurable outcomes.",
            "Create a delivery board with owner and due date for every task."
        };

        if (request.TeamMembers.Count > 0)
            actions.Add($"Assign a project lead from: {string.Join(", ", request.TeamMembers)}.");

        if (request.Goal.Contains("mvp", StringComparison.OrdinalIgnoreCase))
            actions.Add("Prioritize only core MVP scope and defer non-essential features.");

        var risks = new List<string>
        {
            "Scope creep without strict change control.",
            "Hidden dependencies causing timeline slippage."
        };

        if (request.Constraints.Count > 0)
            risks.Add($"Constraints to monitor: {string.Join("; ", request.Constraints)}.");

        return new PlanningResponse(
            $"Plan drafted for {request.ProjectName}. Focus on goal '{request.Goal}' with a short, high-impact execution cycle.",
            actions,
            risks);
    }
}
