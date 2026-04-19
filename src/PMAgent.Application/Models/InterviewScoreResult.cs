namespace PMAgent.Application.Models;

public sealed record InterviewScoreResult(
    double Score,
    bool ShouldStop,
    string Rationale,
    IReadOnlyCollection<InterviewScoreDimension>? Dimensions = null,
    string Feedback = "",
    string AnswerQuality = "PARTIAL",
    bool ShouldAskFollowUp = false);