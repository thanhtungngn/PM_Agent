using Microsoft.Extensions.Diagnostics.HealthChecks;
using OllamaSharp;
using PMAgent.Application.Models;

namespace PMAgent.Infrastructure;

/// <summary>
/// ASP.NET Core health check that verifies the Ollama service is reachable
/// and that the configured model is pulled.
/// Registered only when <see cref="LlmProvider.Ollama"/> is active.
/// </summary>
public sealed class OllamaHealthCheck : IHealthCheck
{
    private readonly string _baseUrl;
    private readonly string _model;

    public OllamaHealthCheck(LlmSettings settings)
    {
        _baseUrl = settings.OllamaBaseUrl;
        _model   = settings.OllamaModel;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var client = new OllamaApiClient(_baseUrl);

        try
        {
            var isRunning = await client.IsRunningAsync(cancellationToken);
            if (!isRunning)
                return HealthCheckResult.Unhealthy(
                    $"Ollama is not running at '{_baseUrl}'. Start it with: ollama serve",
                    data: BaseData());

            var version = await client.GetVersionAsync(cancellationToken);
            var models  = await client.ListLocalModelsAsync(cancellationToken);

            var available  = models.Select(m => m.Name).ToList();
            var modelReady = available.Any(m =>
                m.StartsWith(_model, StringComparison.OrdinalIgnoreCase));

            var data = new Dictionary<string, object>
            {
                ["baseUrl"]         = _baseUrl,
                ["ollamaVersion"]   = version?.ToString() ?? "unknown",
                ["configuredModel"] = _model,
                ["modelPulled"]     = modelReady,
                ["availableModels"] = available
            };

            return modelReady
                ? HealthCheckResult.Healthy(
                    $"Ollama {version} is running. Model '{_model}' is ready.", data)
                : HealthCheckResult.Degraded(
                    $"Ollama is running but model '{_model}' is not pulled. Run: ollama pull {_model}",
                    data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Cannot reach Ollama at '{_baseUrl}': {ex.Message}. Install from https://ollama.com/download then run: ollama serve",
                exception: ex,
                data: BaseData());
        }
    }

    private Dictionary<string, object> BaseData() => new()
    {
        ["baseUrl"]         = _baseUrl,
        ["configuredModel"] = _model
    };
}
