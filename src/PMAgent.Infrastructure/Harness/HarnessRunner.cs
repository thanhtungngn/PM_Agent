using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models;
using PMAgent.Application.Models.Harness;

namespace PMAgent.Infrastructure.Harness;

/// <summary>
/// Drives the orchestrator against each harness scenario, collects timing,
/// runs assertions, and writes reports via all registered sinks.
/// </summary>
public sealed class HarnessRunner(
    IHarnessScenarioProvider scenarioProvider,
    IOrchestratorAgent orchestrator,
    IHarnessAssertionEngine assertionEngine,
    IEnumerable<IHarnessReportSink> reportSinks,
    ILogger<HarnessRunner> logger) : IHarnessRunner
{
    public async Task<HarnessReport> RunAllAsync(CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        logger.LogInformation("[Harness] Starting run {RunId}", runId);

        var scenarios = scenarioProvider.GetScenarios();
        var results = new List<HarnessScenarioResult>(scenarios.Count);

        foreach (var scenario in scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunScenarioCoreAsync(scenario, cancellationToken);
            results.Add(result);
            logger.LogInformation(
                "[Harness] Scenario {Id}: {Status} in {Duration:F1}s",
                scenario.ScenarioId,
                result.Passed ? "PASS" : "FAIL",
                result.TotalDuration.TotalSeconds);
        }

        sw.Stop();
        var passed = results.Count(r => r.Passed);
        var report = new HarnessReport(
            RunId: runId,
            StartedAt: startedAt,
            FinishedAt: DateTimeOffset.UtcNow,
            TotalScenarios: results.Count,
            PassedScenarios: passed,
            FailedScenarios: results.Count - passed,
            PassRatePercent: results.Count == 0 ? 0 : (double)passed / results.Count * 100,
            TotalDuration: sw.Elapsed,
            ScenarioResults: results);

        logger.LogInformation(
            "[Harness] Run {RunId} finished. {Passed}/{Total} passed ({Rate:F1}%) in {Duration:F1}s",
            runId, passed, results.Count, report.PassRatePercent, sw.Elapsed.TotalSeconds);

        foreach (var sink in reportSinks)
            await sink.WriteAsync(report, cancellationToken);

        return report;
    }

    public async Task<HarnessScenarioResult> RunScenarioAsync(
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        var scenario = scenarioProvider.GetScenarios()
            .FirstOrDefault(s => string.Equals(s.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Harness scenario '{scenarioId}' was not found.");

        var result = await RunScenarioCoreAsync(scenario, cancellationToken);

        // Write sinks for individual run too
        var singleReport = new HarnessReport(
            RunId: Guid.NewGuid().ToString("N")[..12],
            StartedAt: DateTimeOffset.UtcNow - result.TotalDuration,
            FinishedAt: DateTimeOffset.UtcNow,
            TotalScenarios: 1,
            PassedScenarios: result.Passed ? 1 : 0,
            FailedScenarios: result.Passed ? 0 : 1,
            PassRatePercent: result.Passed ? 100 : 0,
            TotalDuration: result.TotalDuration,
            ScenarioResults: [result]);

        foreach (var sink in reportSinks)
            await sink.WriteAsync(singleReport, cancellationToken);

        return result;
    }

    private async Task<HarnessScenarioResult> RunScenarioCoreAsync(
        HarnessScenario scenario,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var sw = Stopwatch.StartNew();

        logger.LogInformation(
            "[Harness:{CorrelationId}] Running scenario '{ScenarioId}'",
            correlationId, scenario.ScenarioId);

        try
        {
            var request = new OrchestrationRequest(
                ProjectBrief: scenario.ProjectBrief,
                Context: scenario.Context,
                MaxIterationsPerAgent: scenario.MaxIterationsPerAgent,
                Workflow: scenario.Workflow,
                JobDescription: scenario.JobDescription ?? string.Empty,
                CandidateCv: scenario.CandidateCv ?? string.Empty,
                TechnicalInterviewRoles: scenario.TechnicalInterviewRole is { } role
                    ? [role]
                    : null);

            OrchestrationResult orchestrationResult;
            try
            {
                orchestrationResult = await orchestrator.RunAsync(request, cancellationToken);
            }
            catch (Exception ex) when (scenario.SimulateLlmFault)
            {
                // Expected fault — scenario passes if exception is caught gracefully
                logger.LogWarning(
                    "[Harness:{CorrelationId}] Expected LLM fault caught: {Message}",
                    correlationId, ex.Message);

                return new HarnessScenarioResult(
                    ScenarioId: scenario.ScenarioId,
                    Description: scenario.Description,
                    Passed: true,
                    CorrelationId: correlationId,
                    TotalDuration: sw.Elapsed,
                    RoleResults: [],
                    Assertions: [],
                    ErrorMessage: $"Expected fault caught: {ex.Message}");
            }

            // Collect per-role results and run assertions
            var roleResults = new List<HarnessRoleResult>();
            var allAssertions = new List<HarnessAssertion>();

            foreach (var agentOutput in orchestrationResult.AgentOutputs)
            {
                var roleSw = Stopwatch.StartNew();
                var expectedSections = scenario.ExpectedSections.TryGetValue(agentOutput.Role, out var s) ? s : [];

                var assertions = assertionEngine.Assert(
                    scenario.ScenarioId,
                    agentOutput.Role,
                    agentOutput.Output,
                    expectedSections,
                    agentOutput.Decision,
                    agentOutput.Confidence);

                allAssertions.AddRange(assertions);

                roleResults.Add(new HarnessRoleResult(
                    Role: agentOutput.Role,
                    Success: agentOutput.Success,
                    Duration: roleSw.Elapsed,
                    Output: agentOutput.Output));
            }

            var scenarioPassed = allAssertions.All(a => a.Status != HarnessAssertionStatus.Fail);
            sw.Stop();

            return new HarnessScenarioResult(
                ScenarioId: scenario.ScenarioId,
                Description: scenario.Description,
                Passed: scenarioPassed,
                CorrelationId: correlationId,
                TotalDuration: sw.Elapsed,
                RoleResults: roleResults,
                Assertions: allAssertions);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "[Harness:{CorrelationId}] Scenario '{ScenarioId}' threw unexpectedly",
                correlationId, scenario.ScenarioId);

            return new HarnessScenarioResult(
                ScenarioId: scenario.ScenarioId,
                Description: scenario.Description,
                Passed: false,
                CorrelationId: correlationId,
                TotalDuration: sw.Elapsed,
                RoleResults: [],
                Assertions: [],
                ErrorMessage: ex.Message);
        }
    }
}
