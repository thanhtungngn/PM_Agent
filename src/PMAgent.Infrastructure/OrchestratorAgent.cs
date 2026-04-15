using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;

namespace PMAgent.Infrastructure;

/// <summary>
/// Coordinates the five specialized agents (PO → PM → BA → DEV → TEST) in sequence,
/// forwarding accumulated context to each subsequent agent and aggregating all outputs
/// into a single <see cref="OrchestrationResult"/>.
/// </summary>
public sealed class OrchestratorAgent : IOrchestratorAgent
{
    // Ordered dispatch sequence: PO → PM → BA → DEV → TEST
    private static readonly string[] AgentOrder = ["PO", "PM", "BA", "DEV", "TEST"];

    private readonly IReadOnlyDictionary<string, ISpecializedAgent> _agents;
    private readonly ILogger<OrchestratorAgent> _logger;

    public OrchestratorAgent(IEnumerable<ISpecializedAgent> agents, ILogger<OrchestratorAgent> logger)
    {
        _agents = agents.ToDictionary(a => a.Role, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<OrchestrationResult> RunAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectBrief))
            throw new ArgumentException("ProjectBrief cannot be empty.", nameof(request));

        _logger.LogInformation("[Orchestrator] Starting run. Brief: {Brief}", request.ProjectBrief);
        var totalSw = Stopwatch.StartNew();

        var results = new List<AgentTaskResult>();
        var accumulatedContext = request.Context;

        foreach (var role in AgentOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_agents.TryGetValue(role, out var agent))
            {
                _logger.LogWarning("[Orchestrator] No agent registered for role '{Role}'. Skipping.", role);
                continue;
            }

            _logger.LogInformation("[Orchestrator] Dispatching to {Role} agent.", role);
            var sw = Stopwatch.StartNew();

            var task = new AgentTask(role, request.ProjectBrief, accumulatedContext);
            var result = await agent.ExecuteAsync(task, cancellationToken);
            sw.Stop();

            _logger.LogInformation(
                "[Orchestrator] {Role} agent completed in {ElapsedMs} ms. Success: {Success}",
                role, sw.ElapsedMilliseconds, result.Success);

            results.Add(result);

            // Forward this agent's output as context to the next agent.
            accumulatedContext = $"{accumulatedContext}\n\n[{role}]:\n{result.Output}";
        }

        totalSw.Stop();
        _logger.LogInformation(
            "[Orchestrator] Run complete. {AgentCount} agents, total {ElapsedMs} ms.",
            results.Count, totalSw.ElapsedMilliseconds);

        var summary = BuildSummary(request.ProjectBrief, results);
        return new OrchestrationResult(summary, results);
    }

    private static string BuildSummary(string projectBrief, List<AgentTaskResult> results)
    {
        var roles = string.Join(", ", results.Select(r => r.Role));
        return $"""
            # Project Orchestration Summary

            **Brief:** {projectBrief}

            **Agents consulted:** {roles}

            **Status:** {(results.All(r => r.Success) ? "All agents completed successfully." : "One or more agents reported an issue — review individual outputs.")}

            ## What Was Produced
            {string.Join("\n", results.Select(r => $"- **{r.Role}:** {FirstLine(r.Output)}"))}

            Review each agent's section above for the full detail.
            """;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l));
        return line?.Trim() ?? string.Empty;
    }
}
