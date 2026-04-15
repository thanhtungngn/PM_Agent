using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;

namespace PMAgent.Infrastructure;

/// <summary>
/// LLM client backed by the OpenAI Chat Completions API.
/// </summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    private readonly ChatClient _chatClient;
    private readonly string _model;
    private readonly ILogger<OpenAiLlmClient> _logger;

    public OpenAiLlmClient(LlmSettings settings, ILogger<OpenAiLlmClient> logger)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                "LlmSettings.ApiKey is not configured. " +
                "Add it to appsettings.Development.json under the \"LlmSettings\" section.");

        _model = settings.Model;
        _logger = logger;
        var openAiClient = new OpenAIClient(settings.ApiKey);
        _chatClient = openAiClient.GetChatClient(_model);
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[LLM] Sending request to model '{Model}'. User prompt length: {Chars} chars.",
            _model, userPrompt.Length);

        var sw = Stopwatch.StartNew();
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var result = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        sw.Stop();

        var text = result.Value.Content[0].Text;
        _logger.LogInformation("[LLM] Response received from '{Model}' in {ElapsedMs} ms. Response length: {Chars} chars.",
            _model, sw.ElapsedMilliseconds, text.Length);

        return text;
    }
}
