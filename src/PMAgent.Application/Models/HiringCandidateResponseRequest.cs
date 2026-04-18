namespace PMAgent.Application.Models;

public sealed record HiringCandidateResponseRequest(
    string Message,
    /// <summary>
    /// When true the candidate is explicitly asking for a hint rather than providing a full answer.
    /// </summary>
    bool IsHintRequest = false);