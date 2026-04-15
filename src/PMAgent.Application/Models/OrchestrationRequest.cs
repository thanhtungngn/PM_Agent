namespace PMAgent.Application.Models;

/// <summary>
/// Input to the orchestrator describing the project to be planned.
/// </summary>
public sealed record OrchestrationRequest(
    string ProjectBrief,
    string Context = "",
    int MaxIterationsPerAgent = 10);
