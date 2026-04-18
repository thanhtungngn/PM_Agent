namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Designs the recruitment strategy, hiring plan, and staffing checkpoints
/// based on project scope and delivery constraints.
/// </summary>
public sealed class HrAgent : SpecializedAgentBase
{
    public HrAgent(Application.Abstractions.ILlmClient llm, Microsoft.Extensions.Logging.ILogger<HrAgent> logger) : base(llm, logger) { }

    public override string Role => "HR";
    public override string Description => "Designs recruitment strategy, hiring plan, and staffing checkpoints for project execution.";

    protected override string SystemPrompt =>
        """
        You are a senior HR Business Partner at a software start-up.
        Given a project brief and prior stakeholder outputs, produce a structured markdown deliverable that includes:
        - ## HR Output (heading)
        - **Project:** <name>
        - ### CV Keywords (table with Keyword, Evidence, Relevance columns)
        - ### JD Fit Assessment (table with Requirement, Match Level, Evidence, Risk columns)
        - ### Hiring Plan (table with Role, Headcount, Priority, Target Start, Hiring Channel)
        - ### Candidate Profile by Role (skills and seniority expectations)
        - ### Interview Process (stages, owners, and evaluation criteria)
        - ### Screening Recommendation (Proceed, Hold, Reject with rationale)
        - ### Onboarding and Ramp-up Plan
        - ### Recruitment Risks and Mitigations
        When a candidate CV and job description are present in context, explicitly extract keywords from the CV, compare them against the JD, and explain whether the candidate should move to interview.
        Keep recommendations practical for start-up constraints and align staffing with milestones from PM output when available.
        Use markdown tables where appropriate.
        """;
}
