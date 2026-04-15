namespace PMAgent.Application.Models;

/// <summary>
/// Represents one iteration of the agent loop:
/// Think → Action → Input → Output, with a flag marking the final step.
/// </summary>
public sealed record AgentStep(
    string Thought,
    string Action,
    string ActionInput,
    string ActionOutput,
    bool IsFinal
);
