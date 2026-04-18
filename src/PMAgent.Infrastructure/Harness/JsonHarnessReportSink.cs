using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models.Harness;

namespace PMAgent.Infrastructure.Harness;

/// <summary>
/// Writes a machine-readable JSON report to <c>harness-reports/</c>.
/// </summary>
public sealed class JsonHarnessReportSink(ILogger<JsonHarnessReportSink> logger) : IHarnessReportSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task WriteAsync(HarnessReport report, CancellationToken cancellationToken = default)
    {
        var outputDir = ResolveOutputDir();
        Directory.CreateDirectory(outputDir);

        var filePath = Path.Combine(outputDir, $"harness-{report.RunId}.json");
        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        logger.LogInformation("[Harness] JSON report written to {Path}", filePath);
    }

    private static string ResolveOutputDir()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "harness-reports");
            if (Directory.Exists(current.FullName))
                return candidate;
            current = current.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "harness-reports");
    }
}
