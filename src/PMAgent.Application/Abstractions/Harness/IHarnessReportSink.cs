using PMAgent.Application.Models.Harness;

namespace PMAgent.Application.Abstractions.Harness;

/// <summary>
/// Persists or streams a completed harness report.
/// Multiple sinks can be registered (e.g., JSON + Markdown simultaneously).
/// </summary>
public interface IHarnessReportSink
{
    Task WriteAsync(HarnessReport report, CancellationToken cancellationToken = default);
}
