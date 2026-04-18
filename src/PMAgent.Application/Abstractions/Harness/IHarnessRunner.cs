using PMAgent.Application.Models.Harness;

namespace PMAgent.Application.Abstractions.Harness;

/// <summary>
/// Executes a set of harness scenarios and returns a full run report.
/// </summary>
public interface IHarnessRunner
{
    /// <summary>
    /// Run all scenarios returned by the scenario provider.
    /// </summary>
    Task<HarnessReport> RunAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Run a specific scenario by ID.
    /// </summary>
    Task<HarnessScenarioResult> RunScenarioAsync(
        string scenarioId,
        CancellationToken cancellationToken = default);
}
