namespace PMAgent.Application.Models;

public sealed record InterviewQuestionTemplate
{
    public string Speaker { get; init; } = string.Empty;
    public string TextTemplate { get; init; } = string.Empty;
    public string? VietnameseTextTemplate { get; init; }
    public string? FollowUpTemplate { get; init; }
    public string? VietnameseFollowUpTemplate { get; init; }
    public List<string> HintKeywords { get; init; } = [];
    public string AppliesToTechnicalRole { get; init; } = string.Empty;
}