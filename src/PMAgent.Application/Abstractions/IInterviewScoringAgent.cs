using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

public interface IInterviewScoringAgent
{
    Task<InterviewScoreResult> EvaluateAsync(
        string projectBrief,
        string jobDescription,
        string technicalInterviewRole,
        IReadOnlyCollection<HiringTranscriptTurn> transcript,
        int candidateResponseCount,
        CancellationToken cancellationToken = default);
}