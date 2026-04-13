using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

public interface IAgentPlanner
{
    Task<PlanningResponse> BuildPlanAsync(PlanningRequest request, CancellationToken cancellationToken = default);
}
