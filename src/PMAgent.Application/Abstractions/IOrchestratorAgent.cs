using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

/// <summary>
/// Coordinates multiple <see cref="ISpecializedAgent"/> instances to produce
/// a complete, role-layered project plan from a single project brief.
/// </summary>
public interface IOrchestratorAgent
{
    Task<OrchestrationResult> RunAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken = default);
}
