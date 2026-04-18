namespace PMAgent.Application.Models;

public sealed record HiringTranscriptTurn(
    string Speaker,
    string Message,
    DateTimeOffset OccurredAt);