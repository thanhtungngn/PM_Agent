namespace PMAgent.Application.Models;

/// <summary>
/// The output produced by a single specialized agent.
/// </summary>
public sealed record AgentTaskResult(
    string Role,
    string Output,
    bool Success);
