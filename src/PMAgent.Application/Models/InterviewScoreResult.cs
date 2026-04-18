namespace PMAgent.Application.Models;

public sealed record InterviewScoreResult(
    double Score,
    bool ShouldStop,
    string Rationale);