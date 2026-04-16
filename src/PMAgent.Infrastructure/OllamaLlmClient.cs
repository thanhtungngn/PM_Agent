using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;
using System.Text;

namespace PMAgent.Infrastructure;

/// <summary>
/// LLM client backed by a locally running Ollama instance.
/// Zero-cost — runs entirely on local hardware. Ideal for development.
/// Uses OllamaSharp's native Chat API for maximum reliability.
/// </summary>
public sealed class OllamaLlmClient : ILlmClient
{
    private readonly IOllamaApiClient _client;
    private readonly string _model;
    private readonly ILogger<OllamaLlmClient> _logger;

    public OllamaLlmClient(LlmSettings settings, ILogger<OllamaLlmClient> logger)
    {
        _model  = settings.OllamaModel;
        _logger = logger;
        _client = new OllamaApiClient(settings.OllamaBaseUrl, settings.OllamaModel);
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[LLM:Ollama] Sending request to model '{Model}'. User prompt length: {Chars} chars.",
            _model, userPrompt.Length);

        var sw = Stopwatch.StartNew();

        var request = new ChatRequest
        {
            Model    = _model,
            Stream   = true,
            Messages =
            [
                new Message(ChatRole.System, systemPrompt),
                new Message(ChatRole.User,   userPrompt)
            ]
        };

        var sb = new StringBuilder();

        await foreach (var chunk in _client.ChatAsync(request, cancellationToken))
        {
            var content = chunk?.Message.Content;
            if (!string.IsNullOrEmpty(content))
                sb.Append(content);
        }

        sw.Stop();
        var text = sb.ToString();

        _logger.LogInformation("[LLM:Ollama] Response received from '{Model}' in {ElapsedMs} ms. Response length: {Chars} chars.",
            _model, sw.ElapsedMilliseconds, text.Length);

        return text;
    }
}
