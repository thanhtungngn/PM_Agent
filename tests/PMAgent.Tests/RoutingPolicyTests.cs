using PMAgent.Application.Models;
using PMAgent.Infrastructure.Services;

namespace PMAgent.Tests;

public class RoutingPolicyTests
{
    private static RuleBasedAgentRoutingPolicy BuildPolicy() => new();

    [Fact]
    public void BuildInitialRoute_PlanningIntent_ReturnsPoPmBa()
    {
        var policy = BuildPolicy();
        var request = new OrchestrationRequest("Create a milestone roadmap for the next release");

        var route = policy.BuildInitialRoute(request);

        Assert.Equal(["PO", "PM", "BA"], route);
    }

    [Fact]
    public void BuildInitialRoute_BuildIntent_ReturnsPoBaDevTest()
    {
        var policy = BuildPolicy();
        var request = new OrchestrationRequest("Design architecture and build API for a billing module");

        var route = policy.BuildInitialRoute(request);

        Assert.Equal(["PO", "BA", "DEV", "TEST"], route);
    }

    [Fact]
    public void BuildInitialRoute_HighComplexity_ReturnsFullChain()
    {
        var policy = BuildPolicy();
        var request = new OrchestrationRequest("Build an enterprise multi-tenant platform with compliance and external integration requirements");

        var route = policy.BuildInitialRoute(request);

        Assert.Equal(["PO", "PM", "HR", "BA", "DEV", "TEST"], route);
    }

    [Fact]
    public void BuildInitialRoute_HiringWorkflow_ReturnsPmHrBa()
    {
        var policy = BuildPolicy();
        var request = new OrchestrationRequest(
            "Prepare recruitment and staffing plan for a new project squad",
            Workflow: "hiring",
            JobDescription: "Need a QA engineer with automation experience.",
            CandidateCv: "Candidate has Playwright and API testing experience.");

        var route = policy.BuildInitialRoute(request);

        Assert.Equal(["PM", "HR", "BA"], route);
    }

    [Fact]
    public void BuildInitialRoute_HiringWorkflowWithTechnicalRoles_ReturnsPmHrBaDevTest()
    {
        var policy = BuildPolicy();
        var request = new OrchestrationRequest(
            "Run technical interviews for engineering candidates",
            Workflow: "hiring",
            JobDescription: "Need backend and QA hiring support.",
            CandidateCv: "Candidate profile data.",
            TechnicalInterviewRoles: ["DEV", "TEST"]);

        var route = policy.BuildInitialRoute(request);

        Assert.Equal(["PM", "HR", "BA", "DEV", "TEST"], route);
    }

    [Fact]
    public void ShouldEarlyStop_StopDecisionWithHighConfidence_ReturnsTrue()
    {
        var policy = BuildPolicy();
        var completed = new List<AgentTaskResult>
        {
            new("PM", "output", true, Decision: "stop", Confidence: 0.9, Issues: [], NextAction: "stop")
        };

        var shouldStop = policy.ShouldEarlyStop(completed);

        Assert.True(shouldStop);
    }

    [Fact]
    public void ShouldFallbackToFullChain_EscalateDecision_ReturnsTrue()
    {
        var policy = BuildPolicy();
        var completed = new List<AgentTaskResult>
        {
            new("BA", "output", true, Decision: "escalate", Confidence: 0.7, Issues: ["Missing constraints"], NextAction: "escalate")
        };

        var shouldFallback = policy.ShouldFallbackToFullChain(completed);

        Assert.True(shouldFallback);
    }
}
