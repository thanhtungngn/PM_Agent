namespace PMAgent.Application.Models;

/// <summary>
/// Selects which LLM backend to use.
/// Set "Ollama" in appsettings.Development.json for zero-cost local inference.
/// Set "OpenAI" in production appsettings / environment variables.
/// </summary>
public enum LlmProvider
{
    OpenAI,
    Ollama
}

/// <summary>
/// Configuration for the LLM backend, bound from the "LlmSettings" config section.
/// </summary>
public sealed class LlmSettings
{
    /// <summary>Selects the active LLM backend. Defaults to OpenAI.</summary>
    public LlmProvider Provider { get; init; } = LlmProvider.OpenAI;

    // ── OpenAI ────────────────────────────────────────────────────────────
    public string ApiKey { get; init; } = string.Empty;
    public string Model  { get; init; } = "gpt-4o";

    // ── Ollama ────────────────────────────────────────────────────────────
    public string OllamaBaseUrl { get; init; } = "http://localhost:11434";
    public string OllamaModel   { get; init; } = "llama3.2";
}
