using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

public interface IInterviewQuestionProvider
{
    Task<IReadOnlyList<InterviewQuestion>> BuildQuestionsAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string interviewLanguage = "EN",
        CancellationToken cancellationToken = default);

    Task<InterviewQuestion> BuildQuestionFromNotesAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string speaker,
        int questionNumber,
        string sessionNotesPath,
        CancellationToken cancellationToken = default);

    Task<string> BuildInterviewerReplyFromNotesAsync(
        HiringSessionStartRequest request,
        string technicalInterviewRole,
        string speaker,
        string candidateQuestion,
        string sessionNotesPath,
        string interviewLanguage = "EN",
        CancellationToken cancellationToken = default);
}