using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions.Harness;
using PMAgent.Infrastructure;

namespace PMAgent.Tests;

/// <summary>
/// LLM-connected harness tests. These require a real LLM key (OpenAI or Ollama)
/// and are designed to run in the nightly CI job.
///
/// Running locally:
///   LlmSettings__Provider=OpenAI LlmSettings__ApiKey=sk-... dotnet test --filter "Category=HarnessLLM"
///
/// The tests are skipped automatically when the LLM key or provider is not configured.
/// </summary>
[Trait("Category", "HarnessLLM")]
public sealed class HarnessLlmTests
{
    // ── DI setup ──────────────────────────────────────────────────────────

    private static IHarnessRunner BuildRunner()
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(config);

        return services.BuildServiceProvider().GetRequiredService<IHarnessRunner>();
    }

    // ── Skip helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a skip reason when no LLM is configured, so tests are gracefully
    /// skipped in pull-request builds that do not have the secret.
    /// </summary>
    private static string? SkipReasonIfNoLlm()
    {
        var provider = Environment.GetEnvironmentVariable("LlmSettings__Provider") ?? "OpenAI";
        if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
            return null; // Ollama requires no key — assume it's running locally

        var key = Environment.GetEnvironmentVariable("LlmSettings__ApiKey") ?? string.Empty;
        return string.IsNullOrWhiteSpace(key)
            ? "LlmSettings__ApiKey is not set — skipping LLM-connected harness tests."
            : null;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAllAsync_WithRealLlm_PassRateIsAtLeast95Percent()
    {
        if (SkipReasonIfNoLlm() is not null) return;

        var runner = BuildRunner();
        var report = await runner.RunAllAsync();

        Assert.NotNull(report);
        Assert.True(
            report.PassRatePercent >= 95.0,
            $"Harness pass rate {report.PassRatePercent:F1}% is below the required 95% threshold. " +
            $"Failed scenarios: {report.FailedScenarios}/{report.TotalScenarios}");
    }

    [Fact]
    public async Task RunScenarioAsync_DeliveryHappy_WithRealLlm_Passes()
    {
        if (SkipReasonIfNoLlm() is not null) return;

        var runner = BuildRunner();
        var result = await runner.RunScenarioAsync("delivery-happy");

        Assert.True(result.Passed, $"delivery-happy failed: {result.ErrorMessage}");
    }

    [Fact]
    public async Task RunScenarioAsync_HiringHappy_WithRealLlm_Passes()
    {
        if (SkipReasonIfNoLlm() is not null) return;

        var runner = BuildRunner();
        var result = await runner.RunScenarioAsync("hiring-happy");

        Assert.True(result.Passed, $"hiring-happy failed: {result.ErrorMessage}");
    }

    [Fact]
    public async Task RunAllAsync_WithRealLlm_ReportWrittenToDisk()
    {
        if (SkipReasonIfNoLlm() is not null) return;

        var runner = BuildRunner();
        var report = await runner.RunAllAsync();

        // At least one report file should exist after a run
        var jsonFiles = Directory.GetFiles("harness-reports", "harness-*.json", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(jsonFiles);

        var mdFiles = Directory.GetFiles("harness-reports", "harness-*.md", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(mdFiles);
    }
}
