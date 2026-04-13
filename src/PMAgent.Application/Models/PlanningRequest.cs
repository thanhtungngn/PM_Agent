namespace PMAgent.Application.Models;

public sealed record PlanningRequest(
    string ProjectName,
    string Goal,
    IReadOnlyCollection<string> Constraints,
    IReadOnlyCollection<string> TeamMembers
);
