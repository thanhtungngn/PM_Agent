using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models.Harness;

namespace PMAgent.Infrastructure.Harness;

/// <summary>
/// Built-in scenario set covering happy path, ambiguous, edge, fault, and hiring workflows.
/// All scenarios are deterministic and can be replayed with a fake LLM client.
/// </summary>
public sealed class DefaultHarnessScenarioProvider : IHarnessScenarioProvider
{
    public IReadOnlyList<HarnessScenario> GetScenarios() =>
    [
        // ── Happy path ──────────────────────────────────────────────────────
        new HarnessScenario(
            ScenarioId: "delivery-happy",
            Description: "Happy path: clear project brief, full context, delivery workflow.",
            ProjectBrief: "Build a self-service analytics dashboard for business users.",
            Context: "Team of 5 engineers, 10-week deadline, budget $80k, stakeholders already aligned.",
            MaxIterationsPerAgent: 5,
            Workflow: "delivery",
            ExpectedSections: new Dictionary<string, IReadOnlyList<string>>
            {
                ["PO"] = ["Vision", "Goals", "User Stories"],
                ["PM"] = ["Milestones", "Resource"],
                ["HR"] = ["Hiring Plan", "Candidate"],
                ["BA"] = ["Requirements", "Use Cases"],
                ["DEV"] = ["Technology", "Architecture"],
                ["TEST"] = ["Test Plan", "Quality"],
            }),

        // ── Ambiguous path ──────────────────────────────────────────────────
        new HarnessScenario(
            ScenarioId: "delivery-ambiguous",
            Description: "Ambiguous path: vague brief, limited context. Agents must still produce output.",
            ProjectBrief: "Improve things.",
            Context: "Small team, limited budget.",
            MaxIterationsPerAgent: 5,
            Workflow: "delivery",
            ExpectedSections: new Dictionary<string, IReadOnlyList<string>>
            {
                ["PO"] = ["Vision"],
                ["PM"] = ["Milestones"],
                ["BA"] = ["Requirements"],
            }),

        // ── Edge path ───────────────────────────────────────────────────────
        new HarnessScenario(
            ScenarioId: "delivery-edge",
            Description: "Edge path: long context, conflicting requirements mentioned.",
            ProjectBrief: "Rewrite the entire legacy monolith into microservices while maintaining zero downtime and freezing all new feature development.",
            Context: "50 engineers, 18-month timeline, strict data residency requirements across 12 regions, conflicting stakeholder priorities between cost reduction and feature velocity.",
            MaxIterationsPerAgent: 5,
            Workflow: "delivery",
            ExpectedSections: new Dictionary<string, IReadOnlyList<string>>
            {
                ["PO"] = ["Vision", "Goals"],
                ["PM"] = ["Milestones", "Risk"],
                ["DEV"] = ["Architecture"],
            }),

        // ── Empty LLM response fault ────────────────────────────────────────
        new HarnessScenario(
            ScenarioId: "fault-empty-llm",
            Description: "Failure path: LLM returns empty content. Orchestrator must not crash.",
            ProjectBrief: "Build a simple todo app.",
            Context: "Solo developer, 2-week sprint.",
            MaxIterationsPerAgent: 3,
            Workflow: "delivery",
            ExpectedSections: new Dictionary<string, IReadOnlyList<string>>(),
            SimulateEmptyLlmResponse: true),

        // ── LLM network fault ───────────────────────────────────────────────
        new HarnessScenario(
            ScenarioId: "fault-llm-exception",
            Description: "Failure path: LLM throws an exception on first call. Runner must capture error and not throw.",
            ProjectBrief: "Build a simple todo app.",
            Context: "Solo developer, 2-week sprint.",
            MaxIterationsPerAgent: 3,
            Workflow: "delivery",
            ExpectedSections: new Dictionary<string, IReadOnlyList<string>>(),
            SimulateLlmFault: true),

        // ── Hiring happy path ───────────────────────────────────────────────
        new HarnessScenario(
            ScenarioId: "hiring-happy",
            Description: "Happy path hiring: strong CV match, DEV technical panel.",
            ProjectBrief: "Hire a senior backend engineer for the platform team.",
            Context: "Remote-first, two interviewers available, immediate start.",
            MaxIterationsPerAgent: 5,
            Workflow: "hiring",
            ExpectedSections: new Dictionary<string, IReadOnlyList<string>>
            {
                ["HR"] = ["Hiring Plan", "Candidate Profile"],
                ["PM"] = ["Milestones"],
                ["BA"] = ["Requirements"],
            },
            JobDescription: "Need C#, ASP.NET Core, PostgreSQL, Docker, API design, and CI/CD experience.",
            CandidateCv: "Built ASP.NET Core APIs with PostgreSQL, Docker, Azure DevOps pipelines, and production support.",
            TechnicalInterviewRole: "DEV"),

        // ── Hiring below threshold ──────────────────────────────────────────
        new HarnessScenario(
            ScenarioId: "hiring-below-threshold",
            Description: "Hiring path: CV does not match JD. HR screening should reject without orchestration.",
            ProjectBrief: "Hire a senior backend engineer.",
            Context: "Remote team.",
            MaxIterationsPerAgent: 5,
            Workflow: "hiring",
            ExpectedSections: new Dictionary<string, IReadOnlyList<string>>(),
            JobDescription: "Need C#, ASP.NET Core, PostgreSQL, Docker, distributed systems, and API design.",
            CandidateCv: "Strong experience in manual Excel reporting and customer support workflows."),
    ];
}
