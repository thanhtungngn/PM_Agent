using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

internal static class HiringSeniorityResolver
{
    private static readonly string[] SupportedLevels = ["JUNIOR", "MID", "SENIOR"];

    public static string ResolveLevel(
        string requestedLevel,
        string projectBrief,
        string jobDescription,
        string candidateCv,
        InterviewScoringSettings settings)
    {
        var normalizedRequested = Normalize(requestedLevel);
        if (SupportedLevels.Contains(normalizedRequested, StringComparer.OrdinalIgnoreCase))
            return normalizedRequested;

        var combined = $"{projectBrief}\n{jobDescription}\n{candidateCv}".ToLowerInvariant();

        if (ContainsAny(combined, ["senior", "lead", "principal", "staff", "architect", "7 years", "8 years", "9 years", "10 years"]))
            return "SENIOR";

        if (ContainsAny(combined, ["junior", "fresher", "graduate", "entry level", "entry-level", "intern", "1 year", "2 years"]))
            return "JUNIOR";

        if (settings.SeniorityProfiles.Any(profile => string.Equals(profile.Level, "MID", StringComparison.OrdinalIgnoreCase)))
            return "MID";

        return settings.SeniorityProfiles.FirstOrDefault()?.Level?.ToUpperInvariant() ?? "MID";
    }

    public static InterviewSeniorityProfile ResolveProfile(string level, InterviewScoringSettings settings)
    {
        var normalized = Normalize(level);
        return settings.SeniorityProfiles.FirstOrDefault(profile => string.Equals(profile.Level, normalized, StringComparison.OrdinalIgnoreCase))
            ?? settings.SeniorityProfiles.FirstOrDefault(profile => string.Equals(profile.Level, "MID", StringComparison.OrdinalIgnoreCase))
            ?? new InterviewSeniorityProfile
            {
                Level = normalized,
                Summary = "Evaluate the candidate against the expected scope for this level.",
                ScoreGuidance = "Use transcript evidence, not keyword overlap."
            };
    }

    private static bool ContainsAny(string text, IEnumerable<string> cues) =>
        cues.Any(cue => text.Contains(cue, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return "AUTO";

        var normalized = level.Trim().ToUpperInvariant();
        return normalized switch
        {
            "AUTO" => "AUTO",
            "JR" => "JUNIOR",
            "MID-LEVEL" => "MID",
            "MIDDLE" => "MID",
            "SR" => "SENIOR",
            _ => normalized
        };
    }
}