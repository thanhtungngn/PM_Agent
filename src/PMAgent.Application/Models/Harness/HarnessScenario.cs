namespace PMAgent.Application.Models.Harness;

/// <summary>
/// A single replayable harness scenario. Includes input, expected content checks, and failure simulation flags.
/// </summary>
public sealed record HarnessScenario(
    string ScenarioId,
    string Description,
    string ProjectBrief,
    string Context,
    int MaxIterationsPerAgent,
    string Workflow,
    /// <summary>Expected markdown sections per role. Key = role name, Value = list of heading strings.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedSections,
    string? JobDescription = null,
    string? CandidateCv = null,
    string? TechnicalInterviewRole = null,
    /// <summary>When true the runner will inject a fake LLM that returns an empty string to test fallback.</summary>
    bool SimulateEmptyLlmResponse = false,
    /// <summary>When true the runner will inject a fake LLM that throws to simulate a network fault.</summary>
    bool SimulateLlmFault = false);
