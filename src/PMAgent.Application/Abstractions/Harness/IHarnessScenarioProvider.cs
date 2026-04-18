using PMAgent.Application.Models.Harness;

namespace PMAgent.Application.Abstractions.Harness;

/// <summary>
/// Provides the set of scenarios for a harness run.
/// Implementations can load from in-memory defaults, JSON files, or a database.
/// </summary>
public interface IHarnessScenarioProvider
{
    IReadOnlyList<HarnessScenario> GetScenarios();
}
