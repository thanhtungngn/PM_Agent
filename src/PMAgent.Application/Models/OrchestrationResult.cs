namespace PMAgent.Application.Models;

/// <summary>
/// The aggregated output produced by the orchestrator after all specialized agents have run.
/// </summary>
public sealed record OrchestrationResult(
    string Summary,
    IReadOnlyCollection<AgentTaskResult> AgentOutputs);
