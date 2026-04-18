namespace PMAgent.Application.Models;

public sealed record HiringSessionResult(
    Guid SessionId,
    string Stage,
    bool RequiresUserApproval,
    string ApprovalType,
    double ScreeningFitScore,
    double InterviewScore,
    string CurrentSpeaker,
    string CurrentPrompt,
    string StatusSummary,
    string TechnicalInterviewRole,
    string NotesDocumentPath,
    IReadOnlyCollection<string> Participants,
    IReadOnlyCollection<HiringTranscriptTurn> Transcript,
    /// <summary>True when the current question has an unused follow-up waiting.</summary>
    bool FollowUpAvailable = false,
    /// <summary>Text of the optional follow-up that the interviewer will ask if the candidate answered the primary question.</summary>
    string? PendingFollowUp = null,
    /// <summary>Relative path to the per-candidate folder that stores keywords and live Q&amp;A.</summary>
    string CandidateFolder = "");