using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

public interface IHiringWorkflowService
{
    Task<HiringSessionResult> StartAsync(
        HiringSessionStartRequest request,
        CancellationToken cancellationToken = default);

    Task<HiringSessionResult?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<HiringSessionResult> ApproveScreeningAsync(
        Guid sessionId,
        HiringApprovalRequest request,
        CancellationToken cancellationToken = default);

    Task<HiringSessionResult> ApproveInterviewScheduleAsync(
        Guid sessionId,
        HiringApprovalRequest request,
        CancellationToken cancellationToken = default);

    Task<HiringSessionResult> SubmitCandidateResponseAsync(
        Guid sessionId,
        HiringCandidateResponseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The candidate explicitly requests a hint for the current question.
    /// The interviewer provides 2-3 keyword hints and the question remains active.
    /// </summary>
    Task<HiringSessionResult> RequestHintAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}