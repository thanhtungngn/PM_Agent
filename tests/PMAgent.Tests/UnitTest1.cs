using PMAgent.Application.Models;
using PMAgent.Infrastructure.Services;

namespace PMAgent.Tests;

public class RuleBasedAgentPlannerTests
{
    [Fact]
    public async Task BuildPlanAsync_ReturnsActionsAndRisks()
    {
        var planner = new RuleBasedAgentPlanner();

        var request = new PlanningRequest(
            "PM Agent",
            "Build MVP for project managers",
            ["2 month deadline"],
            ["Alice", "Bob"]);

        var result = await planner.BuildPlanAsync(request);

        Assert.NotEmpty(result.NextActions);
        Assert.NotEmpty(result.Risks);
        Assert.Contains(result.NextActions, x => x.Contains("MVP", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Risks, x => x.Contains("Constraints", StringComparison.OrdinalIgnoreCase));
    }
}
