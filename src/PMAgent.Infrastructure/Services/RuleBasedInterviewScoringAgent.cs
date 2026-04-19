using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

public sealed class RuleBasedInterviewScoringAgent : IInterviewScoringAgent
{
    private readonly InterviewScoringSettings _settings;

    public RuleBasedInterviewScoringAgent(HiringWorkflowSettings? settings = null)
    {
        _settings = settings?.Scoring ?? HiringWorkflowSettings.CreateDefault().Scoring;
    }

    public Task<InterviewScoreResult> EvaluateAsync(
        string projectBrief,
        string jobDescription,
        string targetSeniority,
        string technicalInterviewRole,
        IReadOnlyCollection<HiringTranscriptTurn> transcript,
        int candidateResponseCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidateAnswers = transcript
            .Where(turn => string.Equals(turn.Speaker, "CANDIDATE", StringComparison.OrdinalIgnoreCase))
            .Select(turn => turn.Message)
            .ToArray();
        var latestAnswer = candidateAnswers.LastOrDefault() ?? string.Empty;
        var lowerLatestAnswer = latestAnswer.ToLowerInvariant();
        var hasStrongNegativeSignal = _settings.NegativeSignals.Any(signal => lowerLatestAnswer.Contains(signal, StringComparison.OrdinalIgnoreCase));
        var hasRoleSignal = _settings.RoleSignals
            .Where(group => string.Equals(group.TechnicalRole, technicalInterviewRole, StringComparison.OrdinalIgnoreCase))
            .SelectMany(group => group.Signals)
            .Any(signal => lowerLatestAnswer.Contains(signal, StringComparison.OrdinalIgnoreCase));

        var currentAnswerScore = hasStrongNegativeSignal
            ? 10
            : hasRoleSignal ? 68 : 45;

        var answerQuality = hasStrongNegativeSignal
            ? "POOR"
            : hasRoleSignal ? "GOOD" : "PARTIAL";

        var shouldAskFollowUp = string.Equals(answerQuality, "GOOD", StringComparison.OrdinalIgnoreCase);

        var answerScores = candidateAnswers.Select(answer =>
        {
            var lowerAnswer = answer.ToLowerInvariant();
            if (_settings.NegativeSignals.Any(signal => lowerAnswer.Contains(signal, StringComparison.OrdinalIgnoreCase)))
                return 10d;

            return _settings.RoleSignals
                .Where(group => string.Equals(group.TechnicalRole, technicalInterviewRole, StringComparison.OrdinalIgnoreCase))
                .SelectMany(group => group.Signals)
                .Any(signal => lowerAnswer.Contains(signal, StringComparison.OrdinalIgnoreCase))
                ? 68d
                : 45d;
        }).ToArray();

        var score = answerScores.Length > 0 ? answerScores.Average() : _settings.EarlyStopThreshold;

        var shouldStop = candidateResponseCount >= _settings.MinimumResponsesBeforeStop
            && currentAnswerScore <= 15
            && score < _settings.EarlyStopThreshold;
        var rationale = hasStrongNegativeSignal
            ? "The latest answer does not provide usable technical evidence, so the evaluation was reduced conservatively."
            : "Semantic interview evaluation could not be completed, so a conservative transcript heuristic was applied to the latest answer.";
        var feedback = hasStrongNegativeSignal
            ? "Thanks. Let's make the next answer more concrete. Please focus on one example and be clear about what you personally built, debugged, tested, or decided."
            : hasRoleSignal
                ? "That gives us a useful example. Stay with that case and be ready to go one level deeper on the trade-offs and how you verified the outcome."
                : "I can see the direction. On the next answer, anchor it in one concrete example and walk us through the stack, your decisions, and what you directly handled.";
        var dimensions = _settings.Dimensions
            .Select(definition => new InterviewScoreDimension(
                definition.Name,
                currentAnswerScore,
                hasStrongNegativeSignal
                    ? "Latest answer lacks enough evidence for strong dimension scoring."
                    : "Fallback scoring estimated this dimension from the latest answer only."))
            .ToArray();

        return Task.FromResult(new InterviewScoreResult(score, shouldStop, rationale, dimensions, feedback, answerQuality, shouldAskFollowUp));
    }
}