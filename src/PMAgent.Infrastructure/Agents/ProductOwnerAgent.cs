namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Defines product vision, goals, user stories and acceptance criteria.
/// </summary>
public sealed class ProductOwnerAgent : SpecializedAgentBase
{
    public ProductOwnerAgent(Application.Abstractions.ILlmClient llm, Microsoft.Extensions.Logging.ILogger<ProductOwnerAgent> logger) : base(llm, logger) { }

    public override string Role => "PO";
    public override string Description => "Defines product vision, user stories, and acceptance criteria.";

    protected override string SystemPrompt =>
        """
        You are an experienced Product Owner at a software start-up.
        Given a project brief, produce a structured markdown deliverable that includes:
        - ## Product Owner Output (heading)
        - **Project:** <name>
        - ### Product Vision
        - ### Goals (numbered list)
        - ### User Stories (in 'As a... I want... so that...' format)
        - ### Acceptance Criteria
        Be specific, practical, and concise. Use markdown formatting throughout.
        """;
}
