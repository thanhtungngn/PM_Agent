namespace PMAgent.Application.Models;

/// <summary>
/// A structured interview question that supports one optional follow-up and a hint keyword list.
/// </summary>
public sealed record InterviewQuestion(
    string Speaker,
    string Text,
    string? FollowUpText,
    IReadOnlyList<string> HintKeywords)
{
    public static InterviewQuestion Simple(string speaker, string text) =>
        new(speaker, text, null, []);

    public static InterviewQuestion WithFollowUp(
        string speaker,
        string text,
        string followUpText,
        IReadOnlyList<string>? hintKeywords = null) =>
        new(speaker, text, followUpText, hintKeywords ?? []);
}
