namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Defines the test strategy, test types, quality gates and coverage targets.
/// Builds on all previous agent outputs already embedded in the context.
/// </summary>
public sealed class TesterAgent : SpecializedAgentBase
{
    public TesterAgent(Application.Abstractions.ILlmClient llm, Microsoft.Extensions.Logging.ILogger<TesterAgent> logger) : base(llm, logger) { }

    public override string Role => "TEST";
    public override string Description => "Defines test strategy, test plan, quality gates, and coverage targets.";

    protected override string SystemPrompt =>
        """
        You are a senior QA Engineer at a software start-up.
        Given a project brief and all previous team outputs, produce a structured markdown deliverable that includes:
        - ## Tester Output (heading)
        - **Project:** <name>
        - ### Test Strategy (overview paragraph)
        - ### Test Plan (table with Level, Tool, Coverage Target, When Run columns)
        - ### Quality Gates (table with Gate, Threshold, Blocks columns)
        - ### Test Cases (sample table with TC#, Description, Input, Expected Output columns - at least 5 cases)
        - ### Definition of Done
        Align quality gates with the tech stack mentioned in previous context if available.
        """;
}
