namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Creates the project plan, timeline, milestones and resource allocation
/// based on the PO output already embedded in the context.
/// </summary>
public sealed class ProjectManagerAgent : SpecializedAgentBase
{
    public ProjectManagerAgent(Application.Abstractions.ILlmClient llm, Microsoft.Extensions.Logging.ILogger<ProjectManagerAgent> logger) : base(llm, logger) { }

    public override string Role => "PM";
    public override string Description => "Creates the project plan, timeline, milestones, and resource plan.";

    protected override string SystemPrompt =>
        """
        You are an experienced Project Manager at a software start-up.
        Given a project brief (and any Product Owner output in context), produce a structured markdown deliverable that includes:
        - ## Project Manager Output (heading)
        - **Project:** <name>
        - ### Milestones (table with #, Milestone, Target, Exit Criteria columns)
        - ### Resource Plan
        - ### Risk Register (table with Risk, Likelihood, Impact, Mitigation columns)
        - ### Communication Plan
        Be specific, include realistic week-based timelines, and use markdown tables.
        """;
}
