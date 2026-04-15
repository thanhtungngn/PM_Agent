namespace PMAgent.Application.Models;

/// <summary>
/// A task dispatched by the orchestrator to a single specialized agent.
/// </summary>
public sealed record AgentTask(
    string Role,
    string Goal,
    string Context);
