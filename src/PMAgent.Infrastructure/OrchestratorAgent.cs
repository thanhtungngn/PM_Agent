using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;

namespace PMAgent.Infrastructure;

/// <summary>
/// Coordinates specialized agents (PO → PM → HR → BA → DEV → TEST) in sequence,
/// forwarding accumulated context to each subsequent agent and aggregating all outputs
/// into a single <see cref="OrchestrationResult"/>.
/// </summary>
public sealed class OrchestratorAgent : IOrchestratorAgent
{
    // Ordered full-chain sequence: PO → PM → HR → BA → DEV → TEST
    private static readonly string[] AgentOrder = ["PO", "PM", "HR", "BA", "DEV", "TEST"];

    private readonly IReadOnlyDictionary<string, ISpecializedAgent> _agents;
    private readonly IAgentRoutingPolicy _routingPolicy;
    private readonly ILogger<OrchestratorAgent> _logger;

    public OrchestratorAgent(
        IEnumerable<ISpecializedAgent> agents,
        IAgentRoutingPolicy routingPolicy,
        ILogger<OrchestratorAgent> logger)
    {
        _agents = agents.ToDictionary(a => a.Role, StringComparer.OrdinalIgnoreCase);
        _routingPolicy = routingPolicy;
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
        var accumulatedContext = BuildInitialContext(request);
        var executedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingRoles = new Queue<string>(_routingPolicy.BuildInitialRoute(request));

        _logger.LogInformation("[Orchestrator] Initial route: {Route}", string.Join(" -> ", pendingRoles));

        while (pendingRoles.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var role = pendingRoles.Dequeue();

            if (!executedRoles.Add(role))
                continue;

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

            if (_routingPolicy.ShouldFallbackToFullChain(results))
            {
                _logger.LogWarning("[Orchestrator] Routing fallback triggered. Enqueuing full chain remainder.");
                EnqueueMissingRoles(pendingRoles, executedRoles, AgentOrder);
            }

            if (_routingPolicy.ShouldEarlyStop(results))
            {
                _logger.LogInformation("[Orchestrator] Early-stop triggered after role {Role}.", role);
                break;
            }
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

    private static void EnqueueMissingRoles(
        Queue<string> pendingRoles,
        HashSet<string> executedRoles,
        IEnumerable<string> fullChain)
    {
        foreach (var role in fullChain)
        {
            if (!executedRoles.Contains(role) && !pendingRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                pendingRoles.Enqueue(role);
        }
    }

    private static string BuildInitialContext(OrchestrationRequest request)
    {
        if (!string.Equals(request.Workflow, "hiring", StringComparison.OrdinalIgnoreCase))
            return request.Context;

        var technicalRoles = request.TechnicalInterviewRoles is null || request.TechnicalInterviewRoles.Count == 0
            ? "None requested"
            : string.Join(", ", request.TechnicalInterviewRoles);

        var hiringContext = $"""
            [Hiring Workflow Context]
            Workflow: hiring
            Requested technical interview roles: {technicalRoles}

            Job Description:
            {request.JobDescription}

            Candidate CV:
            {request.CandidateCv}

            Required workflow:
            1. Read the CV and extract broad evidence plus supporting keywords as initial context only.
            2. Check candidate fit against the job description using overall role alignment, not keyword overlap alone.
            3. Plan and execute the interview process.
            4. If DEV or TEST is requested, generate interview questions and scorecards that evaluate the candidate holistically in that role.
            """;

        return string.IsNullOrWhiteSpace(request.Context)
            ? hiringContext
            : $"{request.Context}\n\n{hiringContext}";
    }
}
