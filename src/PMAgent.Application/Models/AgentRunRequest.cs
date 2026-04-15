namespace PMAgent.Application.Models;

public sealed record AgentRunRequest(
    string Goal,
    string Context,
    int MaxIterations = 10
);
