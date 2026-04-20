using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;
using System.Text.Json;

namespace PMAgent.Infrastructure;

/// <summary>
/// Coordinates specialized agents in sequence, forwarding accumulated context
/// to each subsequent agent and aggregating all outputs into a single
/// <see cref="OrchestrationResult"/>.
///
/// When an <see cref="ILlmClient"/> is provided, the orchestrator asks the LLM
/// after each agent completes what role should run next (LLM-driven routing).
/// If the LLM is unavailable or returns an unparseable response, the orchestrator
/// falls back to the rule-based <see cref="IAgentRoutingPolicy"/> automatically.
/// </summary>
public sealed class OrchestratorAgent : IOrchestratorAgent
{
    // Ordered full-chain sequence: PO → PM → HR → BA → DEV → TEST
    private static readonly string[] AgentOrder = ["PO", "PM", "HR", "BA", "DEV", "TEST"];

    private readonly IReadOnlyDictionary<string, ISpecializedAgent> _agents;
    private readonly IAgentRoutingPolicy _routingPolicy;
    private readonly ILlmClient? _llm;
    private readonly ILogger<OrchestratorAgent> _logger;

    public OrchestratorAgent(
        IEnumerable<ISpecializedAgent> agents,
        IAgentRoutingPolicy routingPolicy,
        ILogger<OrchestratorAgent> logger,
        ILlmClient? llm = null)
    {
        _agents = agents.ToDictionary(a => a.Role, StringComparer.OrdinalIgnoreCase);
        _routingPolicy = routingPolicy;
        _logger = logger;
        _llm = llm;
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

            // ── LLM-DRIVEN ROUTING ────────────────────────────────────────────
            // Ask the LLM what should happen next. Fall back to rule-based policy
            // when the LLM is unavailable or returns an unparseable response.
            var llmDecision = await AskLlmForRoutingDecisionAsync(
                request.ProjectBrief, results, pendingRoles, cancellationToken);

            if (llmDecision is not null)
            {
                _logger.LogInformation(
                    "[Orchestrator] LLM routing decision: {Decision}. Reasoning: {Reasoning}",
                    llmDecision.Decision, llmDecision.Reasoning);

                if (string.Equals(llmDecision.Decision, "stop", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[Orchestrator] LLM decided to stop after role {Role}.", role);
                    break;
                }

                if (string.Equals(llmDecision.Decision, "escalate", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[Orchestrator] LLM escalated to full chain after role {Role}.", role);
                    EnqueueMissingRoles(pendingRoles, executedRoles, AgentOrder);
                }
                else if (!string.IsNullOrWhiteSpace(llmDecision.NextRole)
                    && !executedRoles.Contains(llmDecision.NextRole)
                    && !pendingRoles.Any(r => r.Equals(llmDecision.NextRole, StringComparison.OrdinalIgnoreCase)))
                {
                    pendingRoles.Enqueue(llmDecision.NextRole);
                }
            }
            else
            {
                // ── RULE-BASED FALLBACK ──────────────────────────────────────
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
        }

        totalSw.Stop();
        _logger.LogInformation(
            "[Orchestrator] Run complete. {AgentCount} agents, total {ElapsedMs} ms.",
            results.Count, totalSw.ElapsedMilliseconds);

        var summary = BuildSummary(request.ProjectBrief, results);
        return new OrchestrationResult(summary, results);
    }

    // ── LLM ROUTING ───────────────────────────────────────────────────────────

    private sealed record OrchestratorRoutingDecision(
        string Decision,
        string? NextRole,
        string Reasoning);

    private async Task<OrchestratorRoutingDecision?> AskLlmForRoutingDecisionAsync(
        string projectBrief,
        List<AgentTaskResult> completedResults,
        Queue<string> pendingRoles,
        CancellationToken ct)
    {
        if (_llm is null)
            return null;

        try
        {
            var systemPrompt = BuildRoutingSystemPrompt();
            var userPrompt = BuildRoutingUserPrompt(projectBrief, completedResults, pendingRoles);
            var raw = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);
            return ParseRoutingDecision(raw);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "[Orchestrator] LLM routing call failed. Falling back to rule-based policy.");
            return null;
        }
    }

    private static string BuildRoutingSystemPrompt() =>
        """
        You are an AI orchestration agent managing a virtual project delivery team.
        After each team member completes their work, decide what should happen next.

        ## Available Roles
        - PO (Product Owner): Vision, goals, user stories, acceptance criteria
        - PM (Project Manager): Milestones, timeline, resource plan, risk register
        - HR (Human Resources): Hiring plan, candidate screening, interview process
        - BA (Business Analyst): Functional requirements, use cases, gap analysis
        - DEV (Developer): Tech stack, architecture, API design, implementation approach
        - TEST (Tester): Test plan, quality gates, coverage targets

        ## Instructions
        Review the project brief and all completed outputs.
        Decide: should another role contribute, or is the deliverable complete?
        Respond ONLY with valid JSON — no markdown, no code fences, no extra text.

        ## Response Format
        To route to a specific role:
        {"decision": "continue", "nextRole": "PM", "reasoning": "Brief reason"}

        To stop (deliverable is complete or sufficient):
        {"decision": "stop", "reasoning": "Brief reason"}

        To escalate to the full delivery team:
        {"decision": "escalate", "reasoning": "Brief reason"}
        """;

    private static string BuildRoutingUserPrompt(
        string projectBrief,
        List<AgentTaskResult> completedResults,
        Queue<string> pendingRoles)
    {
        var completed = string.Join(", ", completedResults.Select(r => r.Role));
        var pending = string.Join(", ", pendingRoles);
        var last = completedResults.LastOrDefault();
        var lastSummary = last is null
            ? "N/A"
            : last.Output[..Math.Min(300, last.Output.Length)];

        return $"""
            Project Brief: {projectBrief}

            Completed roles: {(string.IsNullOrEmpty(completed) ? "None" : completed)}
            Pending roles: {(string.IsNullOrEmpty(pending) ? "None" : pending)}

            Last completed role: {last?.Role ?? "None"}
            Last output summary: {lastSummary}
            """;
    }

    private static OrchestratorRoutingDecision? ParseRoutingDecision(string raw)
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

        var decision = root.TryGetProperty("decision", out var dp) ? dp.GetString() ?? "" : "";
        var nextRole = root.TryGetProperty("nextRole", out var np) ? np.GetString() : null;
        var reasoning = root.TryGetProperty("reasoning", out var rp) ? rp.GetString() ?? "" : "";

        return new OrchestratorRoutingDecision(decision, nextRole, reasoning);
    }

    // ── SHARED HELPERS ────────────────────────────────────────────────────────

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
            Temporary rule: ignore TEST interview support for now and continue without that branch.

            Job Description:
            {request.JobDescription}

            Candidate CV:
            {request.CandidateCv}

            Required workflow:
            1. Read the CV and extract broad evidence plus supporting keywords as initial context only.
            2. Check candidate fit against the job description using overall role alignment, not keyword overlap alone.
            3. Plan and execute the interview process.
            4. If DEV is requested, generate interview questions and scorecards that evaluate the candidate holistically in that role.
            5. If TEST is requested, ignore it for now.
            """;

        return string.IsNullOrWhiteSpace(request.Context)
            ? hiringContext
            : $"{request.Context}\n\n{hiringContext}";
    }
}
