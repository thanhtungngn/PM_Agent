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
    private readonly RuleBasedInterviewScoringAgent _fallback = new();

    public LlmInterviewScoringAgent(ILlmClient llmClient, ILogger<LlmInterviewScoringAgent> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<InterviewScoreResult> EvaluateAsync(
        string projectBrief,
        string jobDescription,
        string technicalInterviewRole,
        IReadOnlyCollection<HiringTranscriptTurn> transcript,
        int candidateResponseCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var systemPrompt =
            """
            You are an interview evaluation agent.
            Evaluate the transcript and return only valid JSON matching this schema:
            {
              "score": number,
              "shouldStop": boolean,
              "rationale": string
            }

            Rules:
            - score must be between 0 and 100.
            - shouldStop should be true when the candidate is clearly underperforming and the interview should end early.
            - rationale must be concise and mention strengths and concerns.
            - Return JSON only, no markdown fences.
            """;

        var transcriptText = string.Join("\n\n", transcript.Select(turn => $"[{turn.Speaker}] {turn.Message}"));
        var userPrompt = $"""
            Project brief:
            {projectBrief}

            Job description:
            {jobDescription}

            Technical interviewer:
            {technicalInterviewRole}

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

        return await _fallback.EvaluateAsync(projectBrief, jobDescription, technicalInterviewRole, transcript, candidateResponseCount, cancellationToken);
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
            payload.Rationale.Trim());
    }

    private sealed record InterviewScorePayload(double Score, bool ShouldStop, string Rationale);
}