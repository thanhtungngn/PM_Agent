using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

public sealed class RuleBasedAgentPlanner : IAgentPlanner
{
    public Task<PlanningResponse> BuildPlanAsync(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        var nextActions = new List<string>
        {
            $"Clarify acceptance criteria for '{request.ProjectName}'.",
            "Break work into weekly milestones with measurable outcomes.",
            "Create a delivery board with owner and due date for every task."
        };

        if (request.TeamMembers.Count > 0)
        {
            nextActions.Add($"Assign a project lead from: {string.Join(", ", request.TeamMembers)}.");
        }

        if (request.Goal.Contains("mvp", StringComparison.OrdinalIgnoreCase))
        {
            nextActions.Add("Prioritize only core MVP scope and defer non-essential features.");
        }

        var risks = new List<string>
        {
            "Scope creep without strict change control.",
            "Hidden dependencies causing timeline slippage."
        };

        if (request.Constraints.Count > 0)
        {
            risks.Add($"Constraints to monitor: {string.Join("; ", request.Constraints)}.");
        }

        var summary =
            $"Plan drafted for {request.ProjectName}. Focus on goal '{request.Goal}' with a short, high-impact execution cycle.";

        return Task.FromResult(new PlanningResponse(summary, nextActions, risks));
    }
}
