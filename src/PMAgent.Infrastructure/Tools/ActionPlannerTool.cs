using PMAgent.Application.Abstractions;

namespace PMAgent.Infrastructure.Tools;

public sealed class ActionPlannerTool : IAgentTool
{
    public string Name => "action_planner";
    public string Description => "Creates a structured action plan from scope and risk context.";

    public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        var result =
            "Action plan: " +
            "(1) Clarify acceptance criteria with stakeholders. " +
            "(2) Break work into weekly milestones with measurable outcomes. " +
            "(3) Assign an owner and due date to every task. " +
            "(4) Maintain a risk register and review it weekly.";

        return Task.FromResult(result);
    }
}
