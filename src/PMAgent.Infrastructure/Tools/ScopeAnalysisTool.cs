using PMAgent.Application.Abstractions;

namespace PMAgent.Infrastructure.Tools;

public sealed class ScopeAnalysisTool : IAgentTool
{
    public string Name => "scope_analysis";
    public string Description => "Analyzes the project goal and defines the deliverable scope.";

    public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        var result =
            $"Scope for '{input}': Deliver a working system that satisfies the stated goal. " +
            "Core deliverables must be defined, time-boxed, and measurable. " +
            "Out-of-scope items should be documented to prevent scope creep.";

        return Task.FromResult(result);
    }
}
