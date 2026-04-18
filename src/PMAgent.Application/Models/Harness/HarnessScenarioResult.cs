namespace PMAgent.Application.Models.Harness;

/// <summary>
/// Result of running one harness scenario end-to-end including all assertions and timing.
/// </summary>
public sealed record HarnessScenarioResult(
    string ScenarioId,
    string Description,
    bool Passed,
    string CorrelationId,
    TimeSpan TotalDuration,
    IReadOnlyList<HarnessRoleResult> RoleResults,
    IReadOnlyList<HarnessAssertion> Assertions,
    string? ErrorMessage = null);

/// <summary>
/// Per-role execution result within a scenario run.
/// </summary>
public sealed record HarnessRoleResult(
    string Role,
    bool Success,
    TimeSpan Duration,
    string Output);
