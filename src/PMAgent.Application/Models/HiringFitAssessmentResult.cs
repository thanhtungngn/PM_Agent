namespace PMAgent.Application.Models;

public sealed record HiringFitAssessmentResult(
    double Score,
    bool ShouldAdvance,
    string Summary,
    IReadOnlyCollection<string> Strengths,
    IReadOnlyCollection<string> Gaps);