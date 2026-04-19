namespace PMAgent.Application.Models;

public sealed record HiringWorkflowSettings
{
    public double ScreeningPassThreshold { get; init; } = 70;
    public int HintKeywordCount { get; init; } = 3;
    public int MaxCandidateRequestsPerQuestion { get; init; } = 2;
    public int GeneralQuestionCount { get; init; } = 1;
    public int TechnicalQuestionCount { get; init; } = 8;
    public List<InterviewQuestionTemplate> GeneralQuestions { get; init; } = [];
    public List<InterviewQuestionTemplate> TechnicalQuestions { get; init; } = [];
    public List<InterviewQuestionTemplate> BusinessQuestions { get; init; } = [];
    public List<InterviewQuestionTemplate> ClosingQuestions { get; init; } = [];
    public InterviewScoringSettings Scoring { get; init; } = InterviewScoringSettings.CreateDefault();

    public static HiringWorkflowSettings CreateDefault() => new()
    {
        GeneralQuestions =
        [
            new InterviewQuestionTemplate
            {
                Speaker = "PM",
                TextTemplate = "Please introduce yourself through the parts of your experience that best match these project priorities: {{RequirementFocusText}}. Focus on the systems, tools, and responsibilities you actually owned.",
                VietnameseTextTemplate = "Bạn hãy giới thiệu ngắn gọn về những phần kinh nghiệm phù hợp nhất với các ưu tiên của dự án này: {{RequirementFocusText}}. Hãy tập trung vào hệ thống, công cụ và phần việc bạn trực tiếp đảm nhiệm.",
                FollowUpTemplate = "Which parts of that experience map most directly to {{RequirementFocusText}}, and where do you expect the steepest learning curve?",
                VietnameseFollowUpTemplate = "Trong các kinh nghiệm đó, phần nào khớp nhất với {{RequirementFocusText}}, và bạn nghĩ phần nào sẽ cần thời gian làm quen nhiều nhất?",
                HintKeywords = ["project stack", "owned systems", "production scope", "role fit", "learning curve"]
            }
        ],
        TechnicalQuestions =
        [
            new InterviewQuestionTemplate
            {
                Speaker = "DEV",
                AppliesToTechnicalRole = "DEV",
                TextTemplate = "Pick one concrete backend or platform area from your experience that best matches these current project priorities: {{RequirementFocusText}}. Be explicit about the architecture, the technical decisions, and what you personally implemented.",
                VietnameseTextTemplate = "Hãy chọn một hạng mục backend hoặc platform trong kinh nghiệm của bạn khớp nhất với các ưu tiên hiện tại của dự án: {{RequirementFocusText}}. Hãy nói rõ kiến trúc, các quyết định kỹ thuật và phần bạn trực tiếp triển khai.",
                FollowUpTemplate = "Which trade-offs did you consciously accept in that design, and how did you verify they were the right ones?",
                VietnameseFollowUpTemplate = "Trong thiết kế đó, bạn đã chủ động chấp nhận những trade-off nào, và bạn đã kiểm chứng chúng là hợp lý bằng cách nào?",
                HintKeywords = ["architecture", "implementation", "trade-offs", "validation", "ownership"]
            },
            new InterviewQuestionTemplate
            {
                Speaker = "DEV",
                AppliesToTechnicalRole = "DEV",
                TextTemplate = "Stay close to these JD priorities: {{RequirementFocusText}}. Tell us about a production issue around performance, data, API behavior, or deployment that is most relevant here, and explain how you diagnosed it step by step.",
                VietnameseTextTemplate = "Hãy bám sát các ưu tiên trong JD này: {{RequirementFocusText}}. Hãy kể về một sự cố production liên quan tới performance, data, API hoặc deployment gần nhất với yêu cầu đó, và giải thích cách bạn chẩn đoán từng bước.",
                FollowUpTemplate = "What signals, logs, metrics, or test evidence gave you confidence that the fix actually worked?",
                VietnameseFollowUpTemplate = "Những tín hiệu, log, metric hoặc bằng chứng kiểm thử nào giúp bạn tin rằng cách xử lý đó thực sự hiệu quả?",
                HintKeywords = ["diagnosis", "metrics", "logs", "api", "database"]
            },
            new InterviewQuestionTemplate
            {
                Speaker = "DEV",
                AppliesToTechnicalRole = "DEV",
                TextTemplate = "Given the project brief '{{ProjectBrief}}' and these current priorities: {{RequirementFocusText}}, which area would you inspect first in week one, and which past experience prepares you best for it?",
                VietnameseTextTemplate = "Dựa trên project brief '{{ProjectBrief}}' và các ưu tiên hiện tại: {{RequirementFocusText}}, bạn sẽ kiểm tra phần nào trước trong tuần đầu, và kinh nghiệm nào trước đây giúp bạn sẵn sàng nhất cho phần đó?",
                HintKeywords = ["week one", "technical risk", "unknowns", "stack priorities", "project context"]
            },
            new InterviewQuestionTemplate
            {
                Speaker = "TEST",
                AppliesToTechnicalRole = "TEST",
                TextTemplate = "Pick one product or platform area from your experience that best matches this project's stack and requirements, such as {{AlignedSkillText}}. Explain how you designed the test strategy, what coverage mattered most, and what you personally owned.",
                FollowUpTemplate = "Where did you deliberately go deeper or lighter on coverage, and what evidence supported that decision?",
                HintKeywords = ["test strategy", "coverage", "ownership", "risk", "evidence"]
            },
            new InterviewQuestionTemplate
            {
                Speaker = "TEST",
                AppliesToTechnicalRole = "TEST",
                TextTemplate = "Tell us about a release, regression, or integration issue you investigated on a real system that is closest to this project's requirements. How did you narrow the failure down and coordinate with engineers to unblock delivery?",
                FollowUpTemplate = "What logs, test artefacts, environments, or automation gave you confidence in the final conclusion?",
                HintKeywords = ["investigation", "regression", "automation", "environment", "coordination"]
            },
            new InterviewQuestionTemplate
            {
                Speaker = "TEST",
                AppliesToTechnicalRole = "TEST",
                TextTemplate = "For the project '{{ProjectBrief}}', which quality risks in the stack would you want to assess first, and how would you make those risks visible to the team early?",
                HintKeywords = ["quality risk", "stack", "visibility", "early signal", "prioritisation"]
            }
        ],
        BusinessQuestions = [],
        ClosingQuestions =
        [
            new InterviewQuestionTemplate
            {
                Speaker = "HR",
                TextTemplate = "We are moving to Q/A. What questions do you have for the team, or what else would you like to add before we close?",
                VietnameseTextTemplate = "Bây giờ mình chuyển sang phần Q&A. Bạn có câu hỏi nào cho team, hoặc có điều gì muốn bổ sung trước khi kết thúc không?"
            }
        ],
        Scoring = InterviewScoringSettings.CreateDefault()
    };
}