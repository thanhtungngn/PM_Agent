namespace PMAgent.Application.Models;

public sealed record AgentRunResult(
    string FinalAnswer,
    IReadOnlyCollection<AgentStep> Steps
);
