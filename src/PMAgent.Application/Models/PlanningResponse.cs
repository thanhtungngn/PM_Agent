namespace PMAgent.Application.Models;

public sealed record PlanningResponse(
    string Summary,
    IReadOnlyCollection<string> NextActions,
    IReadOnlyCollection<string> Risks
);
