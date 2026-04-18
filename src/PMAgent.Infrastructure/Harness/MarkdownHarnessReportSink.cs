using System.Text;
using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models.Harness;

namespace PMAgent.Infrastructure.Harness;

/// <summary>
/// Writes a human-readable Markdown report to <c>harness-reports/</c>.
/// </summary>
public sealed class MarkdownHarnessReportSink(ILogger<MarkdownHarnessReportSink> logger) : IHarnessReportSink
{
    public async Task WriteAsync(HarnessReport report, CancellationToken cancellationToken = default)
    {
        var outputDir = ResolveOutputDir();
        Directory.CreateDirectory(outputDir);

        var filePath = Path.Combine(outputDir, $"harness-{report.RunId}.md");
        var md = BuildMarkdown(report);
        await File.WriteAllTextAsync(filePath, md, cancellationToken);

        logger.LogInformation("[Harness] Markdown report written to {Path}", filePath);
    }

    private static string BuildMarkdown(HarnessReport report)
    {
        var sb = new StringBuilder();
        var statusEmoji = report.PassRatePercent >= 95 ? "✅" : "⚠️";

        sb.AppendLine("# Harness Run Report");
        sb.AppendLine();
        sb.AppendLine($"| Field | Value |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Run ID | `{report.RunId}` |");
        sb.AppendLine($"| Started | {report.StartedAt:O} |");
        sb.AppendLine($"| Finished | {report.FinishedAt:O} |");
        sb.AppendLine($"| Duration | {report.TotalDuration.TotalSeconds:F1}s |");
        sb.AppendLine($"| Total Scenarios | {report.TotalScenarios} |");
        sb.AppendLine($"| Passed | {report.PassedScenarios} |");
        sb.AppendLine($"| Failed | {report.FailedScenarios} |");
        sb.AppendLine($"| Pass Rate | {statusEmoji} **{report.PassRatePercent:F1}%** |");
        sb.AppendLine();

        foreach (var scenario in report.ScenarioResults)
        {
            var icon = scenario.Passed ? "✅" : "❌";
            sb.AppendLine($"## {icon} `{scenario.ScenarioId}`");
            sb.AppendLine();
            sb.AppendLine($"> {scenario.Description}");
            sb.AppendLine();
            sb.AppendLine($"- **Correlation ID**: `{scenario.CorrelationId}`");
            sb.AppendLine($"- **Duration**: {scenario.TotalDuration.TotalSeconds:F1}s");
            if (scenario.ErrorMessage is not null)
                sb.AppendLine($"- **Error**: {scenario.ErrorMessage}");
            sb.AppendLine();

            if (scenario.RoleResults.Count > 0)
            {
                sb.AppendLine("### Role Results");
                sb.AppendLine();
                sb.AppendLine("| Role | Success | Duration |");
                sb.AppendLine("|---|---|---|");
                foreach (var r in scenario.RoleResults)
                    sb.AppendLine($"| {r.Role} | {(r.Success ? "✅" : "❌")} | {r.Duration.TotalMilliseconds:F0}ms |");
                sb.AppendLine();
            }

            var failedAssertions = scenario.Assertions
                .Where(a => a.Status == HarnessAssertionStatus.Fail)
                .ToList();

            if (failedAssertions.Count > 0)
            {
                sb.AppendLine("### Failed Assertions");
                sb.AppendLine();
                foreach (var a in failedAssertions)
                    sb.AppendLine($"- **[{a.Role}]** `{a.AssertionName}`: {a.Details}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string ResolveOutputDir()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (Directory.Exists(current.FullName))
                return Path.Combine(current.FullName, "harness-reports");
            current = current.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "harness-reports");
    }
}
