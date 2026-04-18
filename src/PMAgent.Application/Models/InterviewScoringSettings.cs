namespace PMAgent.Application.Models;

public sealed record InterviewScoringSettings
{
    public double BaseScore { get; init; } = 35;
    public double EarlyStopThreshold { get; init; } = 40;
    public int MinimumResponsesBeforeStop { get; init; } = 2;
    public int KeywordHitPoints { get; init; } = 4;
    public double KeywordHitMax { get; init; } = 30;
    public int PositiveSignalPoints { get; init; } = 4;
    public double PositiveSignalMax { get; init; } = 20;
    public int NegativeSignalPenalty { get; init; } = 12;
    public double NegativeSignalMax { get; init; } = 35;
    public List<string> PositiveSignals { get; init; } = [];
    public List<string> NegativeSignals { get; init; } = [];
    public List<InterviewRoleSignalGroup> RoleSignals { get; init; } = [];
    public List<InterviewScoringDimensionDefinition> Dimensions { get; init; } = [];
    public List<InterviewSeniorityProfile> SeniorityProfiles { get; init; } = [];

    public static InterviewScoringSettings CreateDefault() => new()
    {
        PositiveSignals =
        [
            "built", "designed", "implemented", "improved", "optimized", "owned", "led",
            "measured", "explained", "debugged", "validated", "collaborated", "learned"
        ],
        NegativeSignals =
        [
            "don't know", "do not know", "not sure", "no experience", "never worked", "cannot answer"
        ],
        Dimensions =
        [
            new InterviewScoringDimensionDefinition { Name = "communication", Description = "Clarity, structure, and directness of the answer.", Weight = 0.20 },
            new InterviewScoringDimensionDefinition { Name = "problem_solving", Description = "How well the candidate frames problems, reasons through constraints, and reaches a workable approach.", Weight = 0.25 },
            new InterviewScoringDimensionDefinition { Name = "technical_judgment", Description = "Quality of trade-off decisions and role-relevant engineering or QA judgment shown in the answer.", Weight = 0.25 },
            new InterviewScoringDimensionDefinition { Name = "ownership", Description = "Evidence of ownership, initiative, and execution.", Weight = 0.15 },
            new InterviewScoringDimensionDefinition { Name = "collaboration", Description = "How effectively the candidate aligns with teammates, stakeholders, and feedback during delivery.", Weight = 0.15 }
        ],
        RoleSignals =
        [
            new InterviewRoleSignalGroup
            {
                TechnicalRole = "DEV",
                Signals = ["api", "database", "performance", "architecture", "c#", ".net", "scal"],
                PointsPerHit = 3,
                MaxScore = 12,
                SummaryText = "Candidate demonstrates engineering depth aligned with the role."
            },
            new InterviewRoleSignalGroup
            {
                TechnicalRole = "TEST",
                Signals = ["test", "automation", "regression", "quality", "bug", "coverage"],
                PointsPerHit = 3,
                MaxScore = 12,
                SummaryText = "Candidate demonstrates QA depth aligned with the role."
            }
        ],
        SeniorityProfiles =
        [
            new InterviewSeniorityProfile
            {
                Level = "JUNIOR",
                Summary = "Evaluate core fundamentals, coachability, structured thinking, and the ability to contribute with guidance.",
                ExpectedBehaviors = ["explains concrete tasks clearly", "asks clarifying questions", "shows learning mindset", "understands basic trade-offs"],
                ScoreGuidance = "Do not expect broad architecture ownership. Reward solid fundamentals, honesty, and the ability to reason through scoped problems with support."
            },
            new InterviewSeniorityProfile
            {
                Level = "MID",
                Summary = "Evaluate independent execution, sound trade-off judgment on common problems, and reliable collaboration across the team.",
                ExpectedBehaviors = ["owns a feature or service area", "explains trade-offs", "works well with PM/BA/QA", "can debug production issues with structure"],
                ScoreGuidance = "Expect clear examples of end-to-end execution and practical decision-making. Do not require org-level influence or architecture leadership."
            },
            new InterviewSeniorityProfile
            {
                Level = "SENIOR",
                Summary = "Evaluate system-level judgment, ambiguity handling, technical leadership, and the ability to raise the effectiveness of the wider team.",
                ExpectedBehaviors = ["handles ambiguous problems", "balances business and technical trade-offs", "mentors others", "improves team practices and delivery quality"],
                ScoreGuidance = "Expect strong ownership, decision quality, and broader impact. Penalize shallow answers more heavily when the role is senior."
            }
        ]
    };
}

public sealed record InterviewRoleSignalGroup
{
    public string TechnicalRole { get; init; } = string.Empty;
    public List<string> Signals { get; init; } = [];
    public int PointsPerHit { get; init; } = 3;
    public double MaxScore { get; init; } = 12;
    public string SummaryText { get; init; } = string.Empty;
}

public sealed record InterviewScoringDimensionDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double Weight { get; init; } = 0.25;
}

public sealed record InterviewSeniorityProfile
{
    public string Level { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> ExpectedBehaviors { get; init; } = [];
    public string ScoreGuidance { get; init; } = string.Empty;
}