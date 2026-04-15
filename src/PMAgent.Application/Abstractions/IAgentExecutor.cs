using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

/// <summary>
/// Runs the Think → Action → Input → Output loop until IsFinal is true.
/// </summary>
public interface IAgentExecutor
{
    Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default);
}
