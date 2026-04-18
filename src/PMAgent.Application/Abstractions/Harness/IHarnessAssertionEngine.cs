using PMAgent.Application.Models.Harness;

namespace PMAgent.Application.Abstractions.Harness;

/// <summary>
/// Evaluates the output of an orchestration run against a scenario's expected sections and routing rules.
/// </summary>
public interface IHarnessAssertionEngine
{
    /// <summary>
    /// Run all assertions for a single role output.
    /// Returns one <see cref="HarnessAssertion"/> per applied assertion rule.
    /// </summary>
    IReadOnlyList<HarnessAssertion> Assert(
        string scenarioId,
        string role,
        string output,
        IReadOnlyList<string> expectedSections,
        string decision,
        double confidence);
}
