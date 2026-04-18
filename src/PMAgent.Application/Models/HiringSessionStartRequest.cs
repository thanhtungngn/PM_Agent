namespace PMAgent.Application.Models;

public sealed record HiringSessionStartRequest(
    string ProjectBrief,
    string JobDescription,
    string CandidateCv,
    string Context = "",
    string TechnicalInterviewRole = "DEV",
    bool AutoApproveInterviewSchedule = true);