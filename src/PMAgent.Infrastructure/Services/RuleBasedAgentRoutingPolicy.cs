using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

public sealed class RuleBasedAgentRoutingPolicy : IAgentRoutingPolicy
{
    private static readonly string[] FullChain = ["PO", "PM", "BA", "DEV", "TEST"];

    public IReadOnlyList<string> BuildInitialRoute(OrchestrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectBrief))
            return FullChain;

        var brief = request.ProjectBrief.Trim();
        var lower = brief.ToLowerInvariant();

        // For high-complexity initiatives, prefer full-team coverage.
        if (IsHighComplexity(brief, lower))
            return FullChain;

        if (IsPlanningIntent(lower))
            return ["PO", "PM", "BA"];

        if (IsBuildIntent(lower))
            return ["PO", "BA", "DEV", "TEST"];

        if (IsTestingIntent(lower))
            return ["PO", "BA", "TEST"];

        return FullChain;
    }

    public bool ShouldEarlyStop(IReadOnlyCollection<AgentTaskResult> completedResults)
    {
        var last = completedResults.LastOrDefault();
        if (last is null)
            return false;

        return string.Equals(last.Decision, "stop", StringComparison.OrdinalIgnoreCase)
            && last.Confidence >= 0.75;
    }

    public bool ShouldFallbackToFullChain(IReadOnlyCollection<AgentTaskResult> completedResults)
    {
        var last = completedResults.LastOrDefault();
        if (last is null)
            return false;

        if (!last.Success)
            return true;

        if (string.Equals(last.Decision, "escalate", StringComparison.OrdinalIgnoreCase))
            return true;

        return last.Confidence < 0.45;
    }

    private static bool IsPlanningIntent(string lower) =>
        lower.Contains("roadmap")
        || lower.Contains("timeline")
        || lower.Contains("milestone")
        || lower.Contains("plan");

    private static bool IsBuildIntent(string lower) =>
        lower.Contains("build")
        || lower.Contains("architecture")
        || lower.Contains("api")
        || lower.Contains("implementation")
        || lower.Contains("design");

    private static bool IsTestingIntent(string lower) =>
        lower.Contains("test")
        || lower.Contains("qa")
        || lower.Contains("quality")
        || lower.Contains("regression");

    private static bool IsHighComplexity(string brief, string lower) =>
        brief.Length > 220
        || lower.Contains("enterprise")
        || lower.Contains("multi-tenant")
        || lower.Contains("compliance")
        || lower.Contains("migration")
        || lower.Contains("real-time")
        || lower.Contains("distributed")
        || lower.Contains("integration");
}
