using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Text.Json;

namespace PMAgent.Infrastructure.Services;

public sealed class LlmInterviewScoringAgent : IInterviewScoringAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILlmClient _llmClient;
    private readonly ILogger<LlmInterviewScoringAgent> _logger;
    private readonly InterviewScoringSettings _settings;
    private readonly RuleBasedInterviewScoringAgent _fallback;

    public LlmInterviewScoringAgent(
        ILlmClient llmClient,
        HiringWorkflowSettings hiringWorkflowSettings,
        ILogger<LlmInterviewScoringAgent> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
        _settings = hiringWorkflowSettings.Scoring;
        _fallback = new RuleBasedInterviewScoringAgent(hiringWorkflowSettings);
    }

    public async Task<InterviewScoreResult> EvaluateAsync(
        string projectBrief,
        string jobDescription,
        string targetSeniority,
        string technicalInterviewRole,
        IReadOnlyCollection<HiringTranscriptTurn> transcript,
        int candidateResponseCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidateTranscript = string.Join("\n", transcript.Where(turn => string.Equals(turn.Speaker, "CANDIDATE", StringComparison.OrdinalIgnoreCase)).Select(turn => turn.Message));
        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(targetSeniority, projectBrief, jobDescription, candidateTranscript, _settings);
        var seniorityProfile = HiringSeniorityResolver.ResolveProfile(seniorityLevel, _settings);

            var systemPrompt = string.Join("\n", [
                "You are an interview evaluation agent.",
                "Evaluate the transcript and return only valid JSON matching this schema:",
                "{",
                "  \"score\": number,",
                "  \"shouldStop\": boolean,",
                "  \"rationale\": string,",
                "  \"dimensions\": [",
                "    { \"name\": string, \"score\": number, \"summary\": string }",
                "  ]",
                "}",
                string.Empty,
                "Rules:",
                "- score must be between 0 and 100.",
                "- shouldStop should be true only when the candidate is clearly underperforming and the interview should end early.",
                $"- Use this rubric when scoring: base score={_settings.BaseScore:F0}, early-stop threshold={_settings.EarlyStopThreshold:F0}, minimum responses before stop={_settings.MinimumResponsesBeforeStop}.",
                $"- Required dimensions: {string.Join(", ", _settings.Dimensions.Select(dimension => $"{dimension.Name} ({dimension.Description})"))}.",
                $"- Weak lexical cues that may support, but must never determine, the score: positive={string.Join(", ", _settings.PositiveSignals)}; concern={string.Join(", ", _settings.NegativeSignals)}.",
                "- Evaluate the candidate as a whole professional in the target role, not as a keyword checklist.",
                "- Treat JD terms, CV terms, and interviewer hints as context only. Never reward keyword overlap by itself, and never penalize missing exact terms if the candidate demonstrates equivalent understanding in different wording.",
                "- Prioritise demonstrated reasoning, realism, correctness, ownership, collaboration, and learning from the candidate's answers.",
                $"- Calibrate your expectations to the target seniority level: {seniorityLevel}.",
                $"- Seniority summary: {seniorityProfile.Summary}",
                $"- Expected behaviours at this level: {string.Join(", ", seniorityProfile.ExpectedBehaviors)}.",
                $"- Seniority scoring guidance: {seniorityProfile.ScoreGuidance}",
                "- Score dimensions from transcript evidence, not from the question wording alone.",
                "- rationale must be concise and mention strengths and concerns.",
                "- Match the language of the rationale and dimension summaries to the dominant language used in the transcript. If mixed, prefer the candidate-facing interview language.",
                "- Return JSON only, no markdown fences."
            ]);

        var transcriptText = string.Join("\n\n", transcript.Select(turn => $"[{turn.Speaker}] {turn.Message}"));
        var userPrompt = $"""
            Project brief:
            {projectBrief}

            Job description:
            {jobDescription}

            Technical interviewer:
            {technicalInterviewRole}

            Target seniority:
            {seniorityLevel}

            Candidate response count:
            {candidateResponseCount}

            Transcript:
            {transcriptText}
            """;

        try
        {
            var response = await _llmClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
            var parsed = TryParse(response);
            if (parsed is not null)
                return parsed;

            _logger.LogWarning("[InterviewScoring] LLM output could not be parsed. Falling back to rule-based scoring.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[InterviewScoring] LLM evaluation failed. Falling back to rule-based scoring.");
        }

        return await _fallback.EvaluateAsync(projectBrief, jobDescription, targetSeniority, technicalInterviewRole, transcript, candidateResponseCount, cancellationToken);
    }

    private static InterviewScoreResult? TryParse(string response)
    {
        var trimmed = response.Trim();
        if (!trimmed.StartsWith('{'))
        {
            var jsonStart = trimmed.IndexOf('{');
            var jsonEnd = trimmed.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                trimmed = trimmed[jsonStart..(jsonEnd + 1)];
        }

        var payload = JsonSerializer.Deserialize<InterviewScorePayload>(trimmed, JsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Rationale))
            return null;

        return new InterviewScoreResult(
            Math.Clamp(payload.Score, 0, 100),
            payload.ShouldStop,
            payload.Rationale.Trim(),
            payload.Dimensions?.Select(dimension => new InterviewScoreDimension(
                dimension.Name,
                Math.Clamp(dimension.Score, 0, 100),
                dimension.Summary ?? string.Empty)).ToArray());
    }

    private sealed record InterviewScorePayload(double Score, bool ShouldStop, string Rationale, List<InterviewDimensionPayload>? Dimensions);

    private sealed record InterviewDimensionPayload(string Name, double Score, string? Summary);
}