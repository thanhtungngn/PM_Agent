using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

public sealed class RuleBasedInterviewScoringAgent : IInterviewScoringAgent
{
    private static readonly string[] PositiveSignals =
    [
        "built", "designed", "implemented", "improved", "optimized", "owned", "led",
        "tested", "automated", "debugged", "deployed", "scaled", "monitored"
    ];

    private static readonly string[] NegativeSignals =
    [
        "don't know", "do not know", "not sure", "no experience", "never worked", "cannot answer"
    ];

    public Task<InterviewScoreResult> EvaluateAsync(
        string projectBrief,
        string jobDescription,
        string technicalInterviewRole,
        IReadOnlyCollection<HiringTranscriptTurn> transcript,
        int candidateResponseCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidateText = string.Join(" ", transcript
            .Where(t => string.Equals(t.Speaker, "CANDIDATE", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Message))
            .ToLowerInvariant();

        var score = 30.0;
        var concerns = new List<string>();
        var strengths = new List<string>();

        if (candidateText.Length >= 250)
        {
            score += 18;
            strengths.Add("Candidate provided detailed answers.");
        }
        else if (candidateText.Length >= 120)
        {
            score += 10;
            strengths.Add("Candidate provided reasonably detailed answers.");
        }
        else
        {
            score -= 8;
            concerns.Add("Answers are short and may lack depth.");
        }

        var expectedKeywords = ExtractKeywords($"{projectBrief} {jobDescription}");
        var candidateKeywords = ExtractKeywords(candidateText);
        var keywordHits = expectedKeywords.Intersect(candidateKeywords, StringComparer.OrdinalIgnoreCase).Count();
        if (expectedKeywords.Count > 0)
        {
            var overlapScore = Math.Min(30, keywordHits * 4);
            score += overlapScore;
            if (overlapScore >= 16)
                strengths.Add("Candidate language overlaps well with the JD and project brief.");
            else if (overlapScore <= 8)
                concerns.Add("Limited overlap between candidate answers and the JD/project brief.");
        }

        var positiveHits = PositiveSignals.Count(signal => candidateText.Contains(signal, StringComparison.OrdinalIgnoreCase));
        score += Math.Min(20, positiveHits * 4);
        if (positiveHits > 0)
            strengths.Add("Candidate uses delivery-oriented evidence in answers.");

        var negativeHits = NegativeSignals.Count(signal => candidateText.Contains(signal, StringComparison.OrdinalIgnoreCase));
        score -= Math.Min(35, negativeHits * 12);
        if (negativeHits > 0)
            concerns.Add("Candidate shows uncertainty on important topics.");

        if (string.Equals(technicalInterviewRole, "DEV", StringComparison.OrdinalIgnoreCase))
        {
            var devKeywords = new[] { "api", "database", "performance", "architecture", "c#", ".net", "scal" };
            var devHits = devKeywords.Count(candidateText.Contains);
            score += Math.Min(12, devHits * 3);
        }
        else if (string.Equals(technicalInterviewRole, "TEST", StringComparison.OrdinalIgnoreCase))
        {
            var testKeywords = new[] { "test", "automation", "regression", "quality", "bug", "coverage" };
            var testHits = testKeywords.Count(candidateText.Contains);
            score += Math.Min(12, testHits * 3);
        }

        score = Math.Clamp(score, 0, 100);

        var shouldStop = candidateResponseCount >= 2 && score < 40;
        var rationale = $"Score {score:F1}/100. "
            + (strengths.Count > 0 ? $"Strengths: {string.Join(" ", strengths)} " : string.Empty)
            + (concerns.Count > 0 ? $"Concerns: {string.Join(" ", concerns)}" : "No major concerns detected so far.");

        return Task.FromResult(new InterviewScoreResult(score, shouldStop, rationale.Trim()));
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "with", "from", "that", "this", "into", "your", "have", "has",
            "will", "about", "candidate", "project", "team", "need", "role", "experience", "years"
        };

        return text
            .Split([' ', '\n', '\r', '\t', ',', '.', ':', ';', '(', ')', '/', '\\', '-', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 3 && !stopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}