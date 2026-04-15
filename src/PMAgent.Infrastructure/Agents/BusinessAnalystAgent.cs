namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Analyses requirements, produces functional specifications and identifies gaps.
/// Builds on PO and PM outputs already embedded in the context.
/// </summary>
public sealed class BusinessAnalystAgent : SpecializedAgentBase
{
    public BusinessAnalystAgent(Application.Abstractions.ILlmClient llm, Microsoft.Extensions.Logging.ILogger<BusinessAnalystAgent> logger) : base(llm, logger) { }

    public override string Role => "BA";
    public override string Description => "Produces functional requirements, use cases, and gap analysis.";

    protected override string SystemPrompt =>
        """
        You are an experienced Business Analyst at a software start-up.
        Given a project brief and previous stakeholder outputs, produce a structured markdown deliverable that includes:
        - ## Business Analyst Output (heading)
        - **Project:** <name>
        - ### Functional Requirements (table with FR#, Requirement, Priority, Source columns)
        - ### Use Cases (at least 2, each with Actor, Precondition, Main flow, Alternate flow)
        - ### Non-Functional Requirements (performance, availability, security, scalability)
        - ### Gap Analysis (table with Gap, Description, Recommended Action columns)
        Be thorough, traceable, and use markdown tables.
        """;
}
