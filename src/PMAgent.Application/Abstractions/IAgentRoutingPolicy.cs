using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

/// <summary>
/// Defines how the orchestrator selects and adjusts role dispatch routes.
/// </summary>
public interface IAgentRoutingPolicy
{
    IReadOnlyList<string> BuildInitialRoute(OrchestrationRequest request);

    bool ShouldEarlyStop(IReadOnlyCollection<AgentTaskResult> completedResults);

    bool ShouldFallbackToFullChain(IReadOnlyCollection<AgentTaskResult> completedResults);
}
