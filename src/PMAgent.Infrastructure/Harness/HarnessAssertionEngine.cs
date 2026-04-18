using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models.Harness;

namespace PMAgent.Infrastructure.Harness;

/// <summary>
/// Evaluates agent outputs against heading presence, routing metadata validity, and structural rules.
/// </summary>
public sealed class HarnessAssertionEngine : IHarnessAssertionEngine
{
    private static readonly string[] ValidDecisions = ["continue", "stop", "escalate"];

    public IReadOnlyList<HarnessAssertion> Assert(
        string scenarioId,
        string role,
        string output,
        IReadOnlyList<string> expectedSections,
        string decision,
        double confidence)
    {
        var results = new List<HarnessAssertion>();

        // 1. Output is not empty
        results.Add(AssertCondition(
            scenarioId, role, "output_not_empty",
            !string.IsNullOrWhiteSpace(output),
            "Output must not be empty."));

        // 2. Required heading sections exist
        foreach (var section in expectedSections)
        {
            var headingVariants = new[]
            {
                $"## {section}",
                $"# {section}",
                $"### {section}",
                section,
            };
            var found = headingVariants.Any(h =>
                output.Contains(h, StringComparison.OrdinalIgnoreCase));

            results.Add(AssertCondition(
                scenarioId, role, $"section_{Slug(section)}",
                found,
                found ? $"Section '{section}' found." : $"Section '{section}' NOT found in output."));
        }

        // 3. Routing decision is valid
        results.Add(AssertCondition(
            scenarioId, role, "valid_decision",
            ValidDecisions.Contains(decision, StringComparer.OrdinalIgnoreCase),
            $"Decision '{decision}' must be one of: {string.Join(", ", ValidDecisions)}."));

        // 4. Confidence is in range [0.0 .. 1.0]
        results.Add(AssertCondition(
            scenarioId, role, "confidence_in_range",
            confidence is >= 0.0 and <= 1.0,
            $"Confidence {confidence:F2} must be in [0.0..1.0]."));

        return results;
    }

    private static HarnessAssertion AssertCondition(
        string scenarioId, string role, string name, bool pass, string details) =>
        new(scenarioId, role, name,
            pass ? HarnessAssertionStatus.Pass : HarnessAssertionStatus.Fail,
            details);

    private static string Slug(string text) =>
        text.ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('/', '_')
            .Replace('-', '_');
}
