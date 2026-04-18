using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

public interface IHiringFitScoringAgent
{
    Task<HiringFitAssessmentResult> EvaluateAsync(
        string projectBrief,
        string jobDescription,
        string candidateCv,
        string targetSeniority,
        string technicalInterviewRole,
        CancellationToken cancellationToken = default);
}