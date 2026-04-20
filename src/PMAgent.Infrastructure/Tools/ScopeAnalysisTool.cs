using PMAgent.Application.Abstractions;

namespace PMAgent.Infrastructure.Tools;

public sealed class ScopeAnalysisTool : IAgentTool
{
    private readonly ILlmClient _llm;

    private const string SystemPrompt =
        """
        You are a project scope analyst. Given a project goal or context, produce a clear and structured scope definition.
        Your output must include:
        - What is in scope (core deliverables, time-boxed and measurable)
        - What is out of scope (explicitly listed to prevent scope creep)
        - Key assumptions and dependencies
        Keep your response focused, specific, and immediately actionable.
        """;

    public ScopeAnalysisTool(ILlmClient llm)
    {
        _llm = llm;
    }

    public string Name => "scope_analysis";
    public string Description => "Analyzes the project goal and defines the deliverable scope.";

    public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        _llm.CompleteAsync(SystemPrompt, input, cancellationToken);
}
