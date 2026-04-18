namespace PMAgent.Application.Models.Harness;

public enum HarnessAssertionStatus { Pass, Fail, Skipped }

/// <summary>
/// Result of a single assertion evaluated against one agent output.
/// </summary>
public sealed record HarnessAssertion(
    string ScenarioId,
    string Role,
    string AssertionName,
    HarnessAssertionStatus Status,
    string Details);
