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

        var score = candidateResponseCount >= _settings.MinimumResponsesBeforeStop
            ? Math.Max(0, _settings.EarlyStopThreshold - 5)
            : _settings.EarlyStopThreshold;

        var shouldStop = candidateResponseCount >= _settings.MinimumResponsesBeforeStop;
        var rationale = "Semantic interview evaluation could not be completed because the LLM result was unavailable or invalid. Conservative fallback was applied without transcript-based scoring.";
        var dimensions = _settings.Dimensions
            .Select(definition => new InterviewScoreDimension(
                definition.Name,
                score,
                "LLM evaluation unavailable; no transcript-based dimension analysis was performed."))
            .ToArray();

        return Task.FromResult(new InterviewScoreResult(score, shouldStop, rationale, dimensions));
    }
}