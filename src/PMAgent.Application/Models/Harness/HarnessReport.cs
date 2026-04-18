namespace PMAgent.Application.Models.Harness;

/// <summary>
/// Top-level report produced after a full harness run (all scenarios).
/// </summary>
public sealed record HarnessReport(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int TotalScenarios,
    int PassedScenarios,
    int FailedScenarios,
    double PassRatePercent,
    TimeSpan TotalDuration,
    IReadOnlyList<HarnessScenarioResult> ScenarioResults);
