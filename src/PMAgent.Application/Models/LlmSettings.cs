namespace PMAgent.Application.Models;

/// <summary>
/// Configuration for the LLM backend, bound from the "LlmSettings" config section.
/// </summary>
public sealed class LlmSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model  { get; init; } = "gpt-4o";
}
