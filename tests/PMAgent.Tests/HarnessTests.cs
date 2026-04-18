using Microsoft.Extensions.Logging.Abstractions;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models;
using PMAgent.Application.Models.Harness;
using PMAgent.Infrastructure.Harness;

namespace PMAgent.Tests;

/// <summary>
/// Fast harness tests that use a fake orchestrator — no real LLM required.
/// Tag: Harness
/// </summary>
[Trait("Category", "Harness")]
public sealed class HarnessTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns realistic structured markdown for each role so heading assertions can pass.
    /// </summary>
    private sealed class FakeOrchestrator : IOrchestratorAgent
    {
        private static readonly Dictionary<string, string> RoleOutputs = new()
        {
            ["PO"] = "## Vision\nDeliver value.\n## Goals\n- Goal 1\n## User Stories\n- Story 1",
            ["PM"] = "## Milestones\n- Week 1\n## Resource Plan\n- Engineer x 3\n## Risk Register\n- Risk 1",
            ["HR"] = "## Hiring Plan\nHire 2 engineers.\n## Candidate Profile\nSenior .NET developer.\n## Interview Process\nTwo rounds.",
            ["BA"] = "## Requirements\n- Req 1\n## Use Cases\n- UC1",
            ["DEV"] = "## Technology Stack\n.NET 10\n## Architecture\nClean architecture.\n## API Design\nREST.",
            ["TEST"] = "## Test Plan\nUnit + integration.\n## Quality Gates\n90% coverage required.",
        };

        public bool ShouldThrow { get; init; }
        public bool ReturnEmpty { get; init; }

        public Task<OrchestrationResult> RunAsync(
            OrchestrationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
                throw new HttpRequestException("Simulated LLM network fault.");

            var outputs = RoleOutputs.Select(kv =>
                new AgentTaskResult(
                    Role: kv.Key,
                    Output: ReturnEmpty ? string.Empty : kv.Value,
                    Success: !ReturnEmpty,
                    Decision: "continue",
                    Confidence: 0.9)).ToList();

            return Task.FromResult(new OrchestrationResult(
                Summary: ReturnEmpty ? string.Empty : "Plan complete.",
                AgentOutputs: outputs));
        }
    }

    private sealed class InMemoryReportSink : IHarnessReportSink
    {
        public HarnessReport? LastReport { get; private set; }
        public Task WriteAsync(HarnessReport report, CancellationToken cancellationToken = default)
        {
            LastReport = report;
            return Task.CompletedTask;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (IHarnessRunner runner, InMemoryReportSink sink) BuildRunner(
        bool shouldThrow = false,
        bool returnEmpty = false,
        IHarnessScenarioProvider? scenarioProvider = null)
    {
        var orchestrator = new FakeOrchestrator { ShouldThrow = shouldThrow, ReturnEmpty = returnEmpty };
        var assertionEngine = new HarnessAssertionEngine();
        var sink = new InMemoryReportSink();
        var provider = scenarioProvider ?? new DefaultHarnessScenarioProvider();

        var runner = new HarnessRunner(
            provider,
            orchestrator,
            assertionEngine,
            [sink],
            NullLogger<HarnessRunner>.Instance);

        return (runner, sink);
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAllAsync_HappyPath_AllScenariosProduceReport()
    {
        var (runner, sink) = BuildRunner();

        var report = await runner.RunAllAsync();

        Assert.NotNull(report);
        Assert.True(report.TotalScenarios > 0);
        Assert.NotNull(sink.LastReport);
        Assert.Equal(report.RunId, sink.LastReport!.RunId);
    }

    [Fact]
    public async Task RunAllAsync_AllRolesOutputRealContent_PassRate100Percent()
    {
        var (runner, _) = BuildRunner();

        var report = await runner.RunAllAsync();

        // All non-fault scenarios with expected sections should pass
        var nonFaultResults = report.ScenarioResults
            .Where(r => !r.ScenarioId.StartsWith("fault", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(nonFaultResults.All(r => r.Passed),
            "All non-fault scenarios should pass with a well-structured fake orchestrator.");
    }

    [Fact]
    public async Task RunScenarioAsync_DeliveryHappy_ReturnsPassedResult()
    {
        var (runner, sink) = BuildRunner();

        var result = await runner.RunScenarioAsync("delivery-happy");

        Assert.True(result.Passed, "delivery-happy scenario should pass.");
        Assert.Equal("delivery-happy", result.ScenarioId);
        Assert.NotEmpty(result.Assertions);
        Assert.NotNull(sink.LastReport);
    }

    [Fact]
    public async Task RunScenarioAsync_FaultEmptyLlm_OutputNotEmptyAssertionFails()
    {
        var (runner, _) = BuildRunner(returnEmpty: true);

        var result = await runner.RunScenarioAsync("delivery-happy");

        Assert.False(result.Passed, "Scenario with empty LLM output should fail output_not_empty assertion.");
        var failedNames = result.Assertions
            .Where(a => a.Status == HarnessAssertionStatus.Fail)
            .Select(a => a.AssertionName)
            .ToList();
        Assert.Contains("output_not_empty", failedNames);
    }

    [Fact]
    public async Task RunScenarioAsync_FaultLlmException_ScenarioPasses()
    {
        var (runner, _) = BuildRunner(shouldThrow: true);

        // The fault-llm-exception scenario is expected to capture the exception and pass
        var result = await runner.RunScenarioAsync("fault-llm-exception");

        Assert.True(result.Passed, "fault-llm-exception scenario should pass after catching the expected fault.");
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task RunScenarioAsync_UnknownId_Throws()
    {
        var (runner, _) = BuildRunner();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            runner.RunScenarioAsync("scenario-does-not-exist"));
    }

    [Fact]
    public async Task HarnessAssertionEngine_MissingSection_ReportsFailure()
    {
        var engine = new HarnessAssertionEngine();

        var assertions = engine.Assert(
            "test-scenario",
            "PO",
            "## Vision\nSome vision text.",
            ["Vision", "Missing Section"],
            "continue",
            0.8);

        var visionAssertion = assertions.First(a => a.AssertionName == "section_vision");
        var missingAssertion = assertions.First(a => a.AssertionName == "section_missing_section");

        Assert.Equal(HarnessAssertionStatus.Pass, visionAssertion.Status);
        Assert.Equal(HarnessAssertionStatus.Fail, missingAssertion.Status);
    }

    [Fact]
    public async Task HarnessAssertionEngine_InvalidDecision_ReportsFailure()
    {
        var engine = new HarnessAssertionEngine();

        var assertions = engine.Assert(
            "test-scenario",
            "PM",
            "## Milestones\nWeek 1",
            [],
            "invalid_decision",
            0.7);

        var decisionAssertion = assertions.First(a => a.AssertionName == "valid_decision");
        Assert.Equal(HarnessAssertionStatus.Fail, decisionAssertion.Status);
    }

    [Fact]
    public async Task HarnessAssertionEngine_ConfidenceOutOfRange_ReportsFailure()
    {
        var engine = new HarnessAssertionEngine();

        var assertions = engine.Assert(
            "test-scenario",
            "PM",
            "## Milestones\nWeek 1",
            [],
            "continue",
            1.5);

        var confidenceAssertion = assertions.First(a => a.AssertionName == "confidence_in_range");
        Assert.Equal(HarnessAssertionStatus.Fail, confidenceAssertion.Status);
    }

    [Fact]
    public async Task RunAllAsync_ReportContainsCorrelationIdsForAllScenarios()
    {
        var (runner, sink) = BuildRunner();

        var report = await runner.RunAllAsync();

        Assert.All(report.ScenarioResults, r =>
            Assert.False(string.IsNullOrWhiteSpace(r.CorrelationId)));
    }

    [Fact]
    public async Task DefaultScenarioProvider_HasAtLeast7Scenarios()
    {
        var provider = new DefaultHarnessScenarioProvider();
        var scenarios = provider.GetScenarios();

        Assert.True(scenarios.Count >= 7, $"Expected at least 7 scenarios, got {scenarios.Count}.");
    }
}
