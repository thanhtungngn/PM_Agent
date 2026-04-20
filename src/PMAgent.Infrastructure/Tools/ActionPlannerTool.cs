using PMAgent.Application.Abstractions;

namespace PMAgent.Infrastructure.Tools;

public sealed class ActionPlannerTool : IAgentTool
{
    private readonly ILlmClient _llm;

    private const string SystemPrompt =
        """
        You are a project action planner. Given scope and risk context, create a concrete, milestone-based action plan.
        Your plan must include:
        - Prioritised list of next actions with clear owners and due dates
        - Weekly milestones with measurable exit criteria
        - Risk mitigation tasks integrated into the timeline
        - Success metrics to track delivery progress
        Make the plan specific, realistic, and immediately actionable.
        """;

    public ActionPlannerTool(ILlmClient llm)
    {
        _llm = llm;
    }

    public string Name => "action_planner";
    public string Description => "Creates a structured action plan from scope and risk context.";

    public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        _llm.CompleteAsync(SystemPrompt, input, cancellationToken);
}
