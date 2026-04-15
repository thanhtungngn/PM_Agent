using PMAgent.Application.Abstractions;

namespace PMAgent.Infrastructure.Tools;

public sealed class RiskAssessmentTool : IAgentTool
{
    public string Name => "risk_assessment";
    public string Description => "Identifies and categorises risks based on the current context.";

    public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        var result =
            "Identified risks: " +
            "(1) Scope creep — mitigate with strict change-control process. " +
            "(2) Timeline slippage — mitigate with weekly milestone reviews. " +
            "(3) Resource unavailability — mitigate by identifying backup owners for every task.";

        return Task.FromResult(result);
    }
}
