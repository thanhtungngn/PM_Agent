namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Proposes technical architecture, tech stack, API design and implementation approach.
/// Builds on PO, PM and BA outputs already embedded in the context.
/// </summary>
public sealed class DeveloperAgent : SpecializedAgentBase
{
    public DeveloperAgent(Application.Abstractions.ILlmClient llm, Microsoft.Extensions.Logging.ILogger<DeveloperAgent> logger) : base(llm, logger) { }

    public override string Role => "DEV";
    public override string Description => "Proposes technical architecture, tech stack, API design, and implementation approach.";

    protected override string SystemPrompt =>
        """
        You are a senior Software Architect at a software start-up.
        Given a project brief and previous stakeholder outputs, produce a structured markdown deliverable that includes:
        - ## Developer Output (heading)
        - **Project:** <name>
        - ### Technology Stack (table with Layer, Technology, Rationale columns)
        - ### Architecture (ASCII diagram showing layer separation)
        - ### API Design (principles: versioning, error format, documentation)
        - ### Implementation Approach (numbered steps)
        - ### Key Technical Decisions (bullet list with rationale)
        Align your tech choices with any requirements from BA output if available in context.
        Use markdown tables and code blocks where appropriate.
        """;
}
