namespace PMAgent.Application.Models;

public sealed record HiringWorkflowSettings
{
    public double ScreeningPassThreshold { get; init; } = 70;
    public int HintKeywordCount { get; init; } = 3;
    public int GeneralQuestionCount { get; init; } = 2;
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
                TextTemplate = "Please introduce yourself and summarize the experience most relevant to this role."
            },
            new InterviewQuestionTemplate
            {
                Speaker = "PM",
                TextTemplate = "Let me briefly introduce the project: {{ProjectBrief}}. Based on this context, where do you think you could create the most value in the first phase, and why?",
                FollowUpTemplate = "How would you build context, align with the team, and choose the right first milestone?",
                HintKeywords = ["ownership", "context building", "team alignment", "first milestone", "delivery impact"]
            }
        ],
        TechnicalQuestions =
        [
            new InterviewQuestionTemplate
            {
                Speaker = "DEV",
                AppliesToTechnicalRole = "DEV",
                TextTemplate = "Tell us about a real engineering problem you owned that is relevant to this role. Walk through the context, options you considered, the decision you made, and how you knew it worked.",
                FollowUpTemplate = "If you faced the same problem again today, what would you keep and what would you change?",
                HintKeywords = ["problem framing", "trade-offs", "decision making", "validation", "outcome"]
            },
            new InterviewQuestionTemplate
            {
                Speaker = "TEST",
                AppliesToTechnicalRole = "TEST",
                TextTemplate = "Tell us about a quality problem or release risk you owned that is relevant to this role. How did you design the test approach, work with the team, and decide what mattered most before release?",
                FollowUpTemplate = "If the same situation happened again with tighter timelines, what would you do differently?",
                HintKeywords = ["risk assessment", "test approach", "team alignment", "release confidence", "trade-offs"]
            }
        ],
        BusinessQuestions =
        [
            new InterviewQuestionTemplate
            {
                Speaker = "BA",
                TextTemplate = "Imagine you joined the project '{{ProjectBrief}}' and a key stakeholder changed requirements late in the cycle. How would you clarify the real need, align trade-offs with the team, and keep delivery on track?",
                FollowUpTemplate = "What artefacts or updates would you use to keep everyone aligned after that change?",
                HintKeywords = ["impact assessment", "trade-offs", "stakeholder alignment", "decision record", "communication"]
            }
        ],
        ClosingQuestions =
        [
            new InterviewQuestionTemplate
            {
                Speaker = "HR",
                TextTemplate = "We are moving to Q/A. What questions do you have for the team, or what else would you like to add before we close?"
            }
        ],
        Scoring = InterviewScoringSettings.CreateDefault()
    };
}