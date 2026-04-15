namespace PMAgent.Application.Abstractions;

/// <summary>
/// Abstraction over any LLM chat-completion backend.
/// Keeps Infrastructure implementations swappable (OpenAI, Azure OpenAI, Ollama, etc.).
/// </summary>
public interface ILlmClient
{
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
