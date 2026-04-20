using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;

namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Agent-based hiring orchestrator that replaces hard-coded FSM stage transitions
/// with LLM-driven reasoning. Uses <see cref="IAgentMemory"/> to maintain and
/// accumulate context across each reasoning phase: CV analysis, fit assessment,
/// interview planning, and hiring recommendation.
///
/// Role token: <c>HIRING_ORC</c>
/// </summary>
public sealed class HiringOrchestrationAgent : ISpecializedAgent
{
    private readonly ILlmClient _llm;
    private readonly IAgentMemory _memory;
    private readonly ILogger<HiringOrchestrationAgent> _logger;

    private const string SystemPrompt =
        """
        You are a hiring orchestration agent managing the end-to-end candidate evaluation process.
        Using the project brief, job description, and candidate CV provided in the context,
        produce a structured assessment covering all of the following sections:

        ## CV Analysis
        Extract the candidate's skills, years of experience, notable projects, and key delivery evidence.
        Identify strengths and gaps relative to the role.

        ## JD Fit Assessment
        Evaluate the candidate against each core job requirement. Rate each as:
        Strong Match | Partial Match | Gap
        Focus on demonstrated role alignment and concrete delivery capability — not keyword overlap alone.

        ## Screening Decision
        Recommend one of: **Proceed** | **Hold** | **Reject**
        Include a confidence level (0–100%) and a clear, evidence-based rationale.

        ## Interview Plan (when Proceed or Hold)
        Outline interview stages, proposed panel members, estimated duration, and 3–5 key questions per stage.
        Calibrate question depth to the candidate's seniority level.

        ## Final Recommendation
        Synthesise the full assessment in 2–3 sentences with concrete next steps.

        Use markdown tables and headings throughout. Be specific and evidence-based.
        """;

    public HiringOrchestrationAgent(
        ILlmClient llm,
        IAgentMemory memory,
        ILogger<HiringOrchestrationAgent> logger)
    {
        _llm = llm;
        _memory = memory;
        _logger = logger;
    }

    public string Role => "HIRING_ORC";

    public string Description =>
        "Orchestrates the complete hiring process using LLM reasoning and agent memory: " +
        "CV analysis, JD fit scoring, interview planning, and hiring recommendation.";

    public async Task<AgentTaskResult> ExecuteAsync(
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{Role}] Starting. Goal: {Goal}", Role, task.Goal);
        var sw = Stopwatch.StartNew();

        // Seed memory with all available inputs so the LLM has full context.
        _memory.Record("PROJECT_BRIEF", task.Goal);
        if (!string.IsNullOrWhiteSpace(task.Context))
            _memory.Record("HIRING_CONTEXT", task.Context);

        var userMessage = BuildUserMessage(task);
        var output = await _llm.CompleteAsync(SystemPrompt, userMessage, cancellationToken);

        // Persist the assessment to memory so downstream agents can reference it.
        _memory.Record(Role, output);

        sw.Stop();
        _logger.LogInformation("[{Role}] Completed in {ElapsedMs} ms.", Role, sw.ElapsedMilliseconds);
        _logger.LogDebug("[{Role}] Output:\n{Output}", Role, output);

        return new AgentTaskResult(
            Role,
            output,
            true,
            Decision: "continue",
            Confidence: 0.85,
            Issues: [],
            NextAction: "continue");
    }

    private static string BuildUserMessage(AgentTask task) =>
        string.IsNullOrWhiteSpace(task.Context)
            ? $"Project Brief: {task.Goal}"
            : $"Project Brief: {task.Goal}\n\nHiring Context:\n{task.Context}";
}
