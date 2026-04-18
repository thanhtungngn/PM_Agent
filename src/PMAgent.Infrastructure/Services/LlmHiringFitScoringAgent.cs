using System.Text.Json;
using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure.Services;

public sealed class LlmHiringFitScoringAgent(
    ILlmClient llmClient,
    HiringWorkflowSettings settings,
    ILogger<LlmHiringFitScoringAgent> logger) : IHiringFitScoringAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILlmClient _llmClient = llmClient;
    private readonly HiringWorkflowSettings _settings = settings;
    private readonly ILogger<LlmHiringFitScoringAgent> _logger = logger;

    public async Task<HiringFitAssessmentResult> EvaluateAsync(
        string projectBrief,
        string jobDescription,
        string candidateCv,
        string targetSeniority,
        string technicalInterviewRole,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var seniorityLevel = HiringSeniorityResolver.ResolveLevel(targetSeniority, projectBrief, jobDescription, candidateCv, _settings.Scoring);
        var seniorityProfile = HiringSeniorityResolver.ResolveProfile(seniorityLevel, _settings.Scoring);

        var systemPrompt = string.Join("\n", [
            "You are an HR screening evaluator.",
            "Read the project brief, job description, and candidate CV.",
            "Return only valid JSON using this schema:",
            "{",
            "  \"score\": number,",
            "  \"shouldAdvance\": boolean,",
            "  \"summary\": string,",
            "  \"strengths\": [string],",
            "  \"gaps\": [string]",
            "}",
            $"Rules: score must be between 0 and 100; use {_settings.ScreeningPassThreshold:F0} as the recommended pass threshold; judge semantic fit, evidence, and role alignment; do not rely on keyword counting alone.",
            $"Calibrate the assessment to target seniority {seniorityLevel}: {seniorityProfile.Summary}",
            "Return JSON only."
        ]);

        var userPrompt = $"""
            Project brief:
            {projectBrief}

            Technical interviewer role:
            {technicalInterviewRole}

            Target seniority:
            {seniorityLevel}

            Job description:
            {jobDescription}

            Candidate CV:
            {candidateCv}
            """;

        try
        {
            var response = await _llmClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
            var parsed = TryParse(response);
            if (parsed is not null)
                return parsed;

            _logger.LogWarning("[HiringFit] Could not parse LLM screening output. Falling back to a conservative non-evaluating result.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HiringFit] LLM screening failed. Falling back to a conservative non-evaluating result.");
        }

        return BuildFallback();
    }

    private HiringFitAssessmentResult BuildFallback()
    {
        return new HiringFitAssessmentResult(
            Score: 0,
            ShouldAdvance: false,
            Summary: "Semantic HR screening could not be completed because the LLM result was unavailable or invalid.",
            Strengths: [],
            Gaps: ["semantic_screening_unavailable"]);
    }

    private static HiringFitAssessmentResult? TryParse(string response)
    {
        var trimmed = response.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            var jsonStart = trimmed.IndexOf('{');
            var jsonEnd = trimmed.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                trimmed = trimmed[jsonStart..(jsonEnd + 1)];
        }

        var payload = JsonSerializer.Deserialize<FitPayload>(trimmed, JsonOptions);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Summary))
            return null;

        return new HiringFitAssessmentResult(
            Score: Math.Clamp(payload.Score, 0, 100),
            ShouldAdvance: payload.ShouldAdvance,
            Summary: payload.Summary.Trim(),
            Strengths: payload.Strengths ?? [],
            Gaps: payload.Gaps ?? []);
    }

    private sealed record FitPayload(double Score, bool ShouldAdvance, string Summary, List<string>? Strengths, List<string>? Gaps);
}