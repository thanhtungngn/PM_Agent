using PMAgent.Application.Models;
using PMAgent.Application.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using PMAgent.Infrastructure.Services;
using System.IO;

namespace PMAgent.Tests;

public sealed class InterviewSystemTests
{
    [Fact]
    public async Task BuildQuestions_UsesLlmForFullInterviewPack()
    {
        var settings = HiringWorkflowSettings.CreateDefault() with
        {
            GeneralQuestionCount = 1,
            TechnicalQuestionCount = 3
        };

        var provider = new ConfigurableInterviewQuestionProvider(new FakeInterviewLlmClient(), settings);
        var questions = await provider.BuildQuestionsAsync(
            new HiringSessionStartRequest(
                "Rebuild checkout service",
                "Need C#, APIs, PostgreSQL, and Docker experience.",
                "Built C# APIs with PostgreSQL and Docker in production.",
                TechnicalInterviewRole: "DEV"),
            "DEV");

        Assert.Equal(6, questions.Count);
        Assert.Contains("Rebuild checkout service", questions[0].Text);
        Assert.Equal("DEV", questions[1].Speaker);
        Assert.Equal("DEV", questions[2].Speaker);
        Assert.Equal("DEV", questions[3].Speaker);
        Assert.Contains("postgresql", questions[1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("BA", questions[4].Speaker);
        Assert.Equal("HR", questions[5].Speaker);
        Assert.False(string.IsNullOrWhiteSpace(questions[1].FollowUpText));
    }

    [Fact]
    public async Task BuildQuestions_PromptEmphasizesHolisticCandidateEvaluation()
    {
        var llm = new CapturingInterviewLlmClient();
        var provider = new ConfigurableInterviewQuestionProvider(llm, HiringWorkflowSettings.CreateDefault());

        await provider.BuildQuestionsAsync(
            new HiringSessionStartRequest(
                "Tuyen backend engineer",
                "Can mot backend engineer co kha nang phan tich trade-off va lam viec voi team.",
                "Ung vien da tung phu trach API va van hanh production.",
                TargetSeniority: "SENIOR",
                TechnicalInterviewRole: "DEV"),
            "DEV");

        Assert.Contains("whole professional", llm.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not as a keyword checklist", llm.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Match the dominant language", llm.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Level: SENIOR", llm.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at most 20%", llm.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildQuestions_AutoDetectsSeniorLevelFromContext()
    {
        var llm = new CapturingInterviewLlmClient();
        var provider = new ConfigurableInterviewQuestionProvider(llm, HiringWorkflowSettings.CreateDefault());

        await provider.BuildQuestionsAsync(
            new HiringSessionStartRequest(
                "Hire a senior backend engineer",
                "Need a senior engineer who can mentor others and handle ambiguity.",
                "Candidate has 8 years building APIs and leading architecture discussions.",
                TechnicalInterviewRole: "DEV"),
            "DEV");

        Assert.Contains("Level: SENIOR", llm.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildQuestionFromNotes_UsesLiveMarkdownNotes()
    {
        var llm = new CapturingInterviewLlmClient();
        var provider = new ConfigurableInterviewQuestionProvider(llm, HiringWorkflowSettings.CreateDefault());
        var notesPath = Path.Combine(Path.GetTempPath(), $"pmagent-notes-{Guid.NewGuid():N}.md");

        await File.WriteAllTextAsync(notesPath, """
            # Hiring Interview Notes

            - ProjectBrief: Hire a backend engineer

            ## Job Description

            Need a backend engineer who can reason about trade-offs.

            ## Candidate CV

            Built APIs and worked with PostgreSQL in production.

            ## Transcript

            ### PM

            Please introduce yourself.
            """);

        try
        {
            var question = await provider.BuildQuestionFromNotesAsync(
                new HiringSessionStartRequest(
                    "Hire a backend engineer",
                    "Need a backend engineer who can reason about trade-offs.",
                    "Built APIs and worked with PostgreSQL in production.",
                    TechnicalInterviewRole: "DEV"),
                "DEV",
                "PM",
                1,
                notesPath);

            Assert.Equal("PM", question.Speaker);
            Assert.Contains("Live hiring session markdown notes", llm.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Hiring Interview Notes", llm.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(notesPath))
                File.Delete(notesPath);
        }
    }

    [Fact]
    public async Task RuleBasedScoring_FallsBackConservativelyWithoutTranscriptEvaluation()
    {
        var settings = HiringWorkflowSettings.CreateDefault() with
        {
            Scoring = HiringWorkflowSettings.CreateDefault().Scoring with
            {
                EarlyStopThreshold = 50,
                MinimumResponsesBeforeStop = 2
            }
        };

        var scorer = new RuleBasedInterviewScoringAgent(settings);
        var transcript = new List<HiringTranscriptTurn>
        {
            new("CANDIDATE", "I implemented the API before, but I am uncertain about the architecture here.", DateTimeOffset.UtcNow),
            new("CANDIDATE", "I am still uncertain and do not know the best trade-off.", DateTimeOffset.UtcNow)
        };

        var result = await scorer.EvaluateAsync(
            "Build a billing platform",
            "Need API design and architecture depth",
            "MID",
            "DEV",
            transcript,
            candidateResponseCount: 2);

        Assert.Equal(45, result.Score);
        Assert.True(result.ShouldStop);
        Assert.Contains("LLM result was unavailable or invalid", result.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LlmHiringFitScoring_UsesSemanticFitAssessment()
    {
        var settings = HiringWorkflowSettings.CreateDefault();
        var scorer = new LlmHiringFitScoringAgent(new FakeInterviewLlmClient(), settings, NullLogger<LlmHiringFitScoringAgent>.Instance);

        var result = await scorer.EvaluateAsync(
            "Rebuild checkout service",
            "Need C#, ASP.NET Core, PostgreSQL, and Docker experience.",
            "Built C# APIs with PostgreSQL and Docker in production.",
            "MID",
            "DEV");

        Assert.True(result.ShouldAdvance);
        Assert.True(result.Score >= 40);
        Assert.NotEmpty(result.Strengths);
    }

    [Fact]
    public async Task LlmInterviewScoring_ParsesDimensionBreakdown()
    {
        var settings = HiringWorkflowSettings.CreateDefault();
        var scorer = new LlmInterviewScoringAgent(new FakeInterviewLlmClient(), settings, NullLogger<LlmInterviewScoringAgent>.Instance);

        var result = await scorer.EvaluateAsync(
            "Rebuild checkout service",
            "Need C#, ASP.NET Core, PostgreSQL, and Docker experience.",
            "SENIOR",
            "DEV",
            [new HiringTranscriptTurn("CANDIDATE", "I implemented and owned the API design, deployment, and database tuning.", DateTimeOffset.UtcNow)],
            candidateResponseCount: 1);

        Assert.NotNull(result.Dimensions);
        Assert.Contains(result.Dimensions!, dimension => string.Equals(dimension.Name, "technical_judgment", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.Score > 0);
    }

        private sealed class FakeInterviewLlmClient : ILlmClient
        {
                public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
                {
            if (systemPrompt.Contains("HR screening evaluator", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("""
                    {"score": 88, "shouldAdvance": true, "summary": "The CV shows strong semantic alignment with the backend role.", "strengths": ["backend ownership", "postgresql", "docker"], "gaps": ["distributed systems"]}
                    """);
            }

            if (systemPrompt.Contains("continuing an in-progress interview", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(BuildSingleQuestion(userPrompt));
            }

            if (systemPrompt.Contains("expert interviewer", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("""
                    {
                        "questions": [
                            {
                                "speaker": "PM",
                                "text": "Project: Rebuild checkout service. Please introduce yourself through the experience most relevant to this stack.",
                                "followUpText": "Which part of that experience maps most directly to this project's APIs and PostgreSQL workload?",
                                "hintKeywords": ["project stack", "role fit", "owned systems"]
                            },
                            {
                                "speaker": "DEV",
                                "text": "Walk us through a technical decision you made around PostgreSQL performance and API design trade-offs.",
                                "followUpText": "How did you verify that the trade-off was the right one in production?",
                                "hintKeywords": ["trade-offs", "postgresql", "performance"]
                            },
                            {
                                "speaker": "DEV",
                                "text": "Tell us about a production debugging or deployment issue you handled on a backend service and how you narrowed it down.",
                                "followUpText": null,
                                "hintKeywords": ["debugging", "deployment", "signals"]
                            },
                            {
                                "speaker": "DEV",
                                "text": "For Rebuild checkout service, which part of the stack would you inspect first in week one and what technical risks would you surface early?",
                                "followUpText": null,
                                "hintKeywords": ["week one", "risk", "stack priorities"]
                            },
                            {
                                "speaker": "BA",
                                "text": "How would you handle a late requirement change that affects the checkout flow and multiple stakeholders?",
                                "followUpText": null,
                                "hintKeywords": ["impact", "stakeholders", "decision log"]
                            },
                            {
                                "speaker": "HR",
                                "text": "What questions do you have for the team before we close?",
                                "followUpText": null,
                                "hintKeywords": ["team", "success", "expectations"]
                            }
                        ]
                    }
                    """);
            }

                        if (systemPrompt.Contains("interview evaluation agent", StringComparison.OrdinalIgnoreCase))
                        {
                                return Task.FromResult("""
                                        {
                                            "score": 83,
                                            "shouldStop": false,
                                            "rationale": "Strong evidence of technical ownership and clear communication.",
                                            "dimensions": [
                                                { "name": "communication", "score": 80, "summary": "Clear and direct." },
                                                { "name": "problem_solving", "score": 84, "summary": "Breaks down the problem in a practical way." },
                                                { "name": "technical_judgment", "score": 88, "summary": "Shows strong technical judgment." },
                                                { "name": "ownership", "score": 82, "summary": "Demonstrates ownership." },
                                                { "name": "collaboration", "score": 76, "summary": "Shows awareness of delivery alignment." }
                                            ]
                                        }
                                        """);
                        }

                        return Task.FromResult("{}");
                }

                private static string BuildSingleQuestion(string userPrompt)
                {
                    if (userPrompt.Contains("Requested speaker:\nPM", StringComparison.OrdinalIgnoreCase)
                        && userPrompt.Contains("Question number for this speaker:\n1", StringComparison.OrdinalIgnoreCase))
                    {
                        return """
                            { "speaker": "PM", "text": "Project: Rebuild checkout service. Please introduce yourself through the experience most relevant to this stack.", "followUpText": "Which part of that experience maps most directly to this project's APIs and PostgreSQL workload?", "hintKeywords": ["project stack", "role fit", "owned systems"] }
                            """;
                    }

                    if (userPrompt.Contains("Requested speaker:\nDEV", StringComparison.OrdinalIgnoreCase))
                    {
                        if (userPrompt.Contains("Question number for this speaker:\n1", StringComparison.OrdinalIgnoreCase))
                        {
                            return """
                                { "speaker": "DEV", "text": "Walk us through a technical decision you made around PostgreSQL performance and API design trade-offs.", "followUpText": "How did you verify that the trade-off was the right one in production?", "hintKeywords": ["trade-offs", "postgresql", "performance"] }
                                """;
                        }

                        if (userPrompt.Contains("Question number for this speaker:\n2", StringComparison.OrdinalIgnoreCase))
                        {
                            return """
                                { "speaker": "DEV", "text": "Tell us about a production debugging or deployment issue you handled on a backend service and how you narrowed it down.", "followUpText": null, "hintKeywords": ["debugging", "deployment", "signals"] }
                                """;
                        }

                        return """
                            { "speaker": "DEV", "text": "For Rebuild checkout service, which part of the stack would you inspect first in week one and what technical risks would you surface early?", "followUpText": null, "hintKeywords": ["week one", "risk", "stack priorities"] }
                            """;
                    }

                    if (userPrompt.Contains("Requested speaker:\nBA", StringComparison.OrdinalIgnoreCase))
                    {
                        return """
                            { "speaker": "BA", "text": "How would you handle a late requirement change that affects the checkout flow and multiple stakeholders?", "followUpText": null, "hintKeywords": ["impact", "stakeholders", "decision log"] }
                            """;
                    }

                    return """
                        { "speaker": "HR", "text": "What questions do you have for the team before we close?", "followUpText": null, "hintKeywords": ["team", "success", "expectations"] }
                        """;
                }
        }

    private sealed class CapturingInterviewLlmClient : ILlmClient
    {
        public string LastSystemPrompt { get; private set; } = string.Empty;
        public string LastUserPrompt { get; private set; } = string.Empty;

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;
            if (systemPrompt.Contains("continuing an in-progress interview", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("""
                    { "speaker": "PM", "text": "Question 1", "followUpText": null, "hintKeywords": ["impact"] }
                    """);
            }

            return Task.FromResult("""
                {
                    "questions": [
                        { "speaker": "PM", "text": "Question 1", "followUpText": null, "hintKeywords": ["impact"] },
                        { "speaker": "DEV", "text": "Question 2", "followUpText": "Follow up", "hintKeywords": ["ownership"] },
                        { "speaker": "DEV", "text": "Question 3", "followUpText": null, "hintKeywords": ["trade-offs"] },
                        { "speaker": "DEV", "text": "Question 4", "followUpText": null, "hintKeywords": ["debugging"] },
                        { "speaker": "BA", "text": "Question 5", "followUpText": null, "hintKeywords": ["alignment"] },
                        { "speaker": "HR", "text": "Question 6", "followUpText": null, "hintKeywords": ["closing"] }
                    ]
                }
                """);
        }
    }
}