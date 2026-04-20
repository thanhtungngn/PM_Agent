using PMAgent.Application.Abstractions;

namespace PMAgent.Infrastructure.Tools;

public sealed class RiskAssessmentTool : IAgentTool
{
    private readonly ILlmClient _llm;

    private const string SystemPrompt =
        """
        You are a project risk analyst. Given a project context, identify and categorise all significant risks.
        For each risk provide:
        - Risk name and description
        - Category (scope, timeline, resource, technical, or external)
        - Likelihood (Low / Medium / High)
        - Impact (Low / Medium / High)
        - Concrete mitigation strategy
        Present your findings as a structured risk register using markdown tables where appropriate.
        """;

    public RiskAssessmentTool(ILlmClient llm)
    {
        _llm = llm;
    }

    public string Name => "risk_assessment";
    public string Description => "Identifies and categorises risks based on the current context.";

    public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        _llm.CompleteAsync(SystemPrompt, input, cancellationToken);
}
