# PM Agent — Orchestrator Agentic Pattern Roadmap

**Date:** April 15, 2026  
**Pattern:** Orchestrator → Specialized Agents  
**Team simulation:** PO · PM · BA · DEV · TEST

---

## Vision

Evolve the PM Agent from a single-loop ReAct agent into a **multi-agent orchestration system** that mirrors a real start-up software delivery team. A single `OrchestratorAgent` receives a high-level project brief and coordinates five specialized agents — each playing a distinct role — producing a comprehensive, role-layered project plan.

---

## Problem Statement

A single `AgentExecutor` running three generic tools (scope, risk, action) is too coarse-grained for real project delivery. Different roles have different reasoning contexts:

| Role demand | Why a single agent cannot handle it |
|---|---|
| Product vision & user stories | Requires strategic thinking and customer empathy |
| Project timeline & milestones | Requires planning and estimation expertise |
| Functional specs & gap analysis | Requires requirements engineering discipline |
| Technical architecture | Requires engineering knowledge and trade-off reasoning |
| Test strategy & quality gates | Requires QA mindset and coverage analysis |

---

## Architecture Overview

```
                ┌─────────────────────────────────────────┐
                │          OrchestratorAgent               │
                │   Receives ProjectBrief, dispatches      │
                │   tasks and accumulates context          │
                └──────────────┬──────────────────────────┘
                               │  sequential dispatch
              ┌────────────────┼────────────────────────┐
              │                │                        │
     ┌────────▼───┐   ┌────────▼───┐   ┌────────────────▼───┐
     │  PO Agent  │   │  PM Agent  │   │    BA Agent        │
     │ Vision &   │   │ Plan &     │   │ Requirements &     │
     │ User Stories│  │ Timeline   │   │ Functional Specs   │
     └────────────┘   └────────────┘   └────────────────────┘
              │                │                        │
              └────────────────┼────────────────────────┘
                               │ context forwarded
              ┌────────────────┼──────────────┐
              │                               │
     ┌────────▼───┐                 ┌─────────▼──┐
     │  DEV Agent │                 │ TEST Agent │
     │ Architecture│                │ Test Plan  │
     │ & Tech Stack│                │ & Quality  │
     └────────────┘                 └────────────┘
```

**Execution flow:**
1. PO receives project brief → outputs product vision + user stories
2. PM receives brief + PO output → outputs plan + timeline
3. BA receives brief + PO + PM output → outputs functional specs
4. DEV receives brief + PO + PM + BA output → outputs technical architecture
5. TEST receives brief + all above → outputs test strategy
6. Orchestrator aggregates all five outputs into `OrchestrationResult`

---

## Phases & Milestones

### Phase 1 — Contracts & Models *(Foundation)*

**Goal:** Define the data shapes and interfaces the entire system will use.

| Deliverable | Type | Layer |
|---|---|---|
| `AgentTask` | `sealed record` | `PMAgent.Application/Models` |
| `AgentTaskResult` | `sealed record` | `PMAgent.Application/Models` |
| `OrchestrationRequest` | `sealed record` | `PMAgent.Application/Models` |
| `OrchestrationResult` | `sealed record` | `PMAgent.Application/Models` |
| `ISpecializedAgent` | `interface` | `PMAgent.Application/Abstractions` |
| `IOrchestratorAgent` | `interface` | `PMAgent.Application/Abstractions` |

**Done when:** Solution builds with no errors; no implementation yet.

---

### Phase 2 — Specialized Agents *(Core Intelligence)*

**Goal:** Implement one concrete `ISpecializedAgent` per role.

| Agent class | Role token | Primary output |
|---|---|---|
| `ProductOwnerAgent` | `"PO"` | Product vision, goals, user stories, acceptance criteria |
| `ProjectManagerAgent` | `"PM"` | Milestones, timeline, resource plan, risk register |
| `BusinessAnalystAgent` | `"BA"` | Functional requirements, use cases, gap analysis |
| `DeveloperAgent` | `"DEV"` | Tech stack, architecture decisions, API design, implementation approach |
| `TesterAgent` | `"TEST"` | Test plan, test types, quality gates, coverage targets |

Each agent:
- Is rule-based (no LLM dependency) for deterministic, testable output
- Builds on the accumulated `Context` string passed in by the orchestrator
- Returns a rich markdown-formatted `Output` string

**Done when:** All five agents implement `ISpecializedAgent` and compile.

---

### Phase 3 — Orchestrator *(Coordination Layer)*

**Goal:** Implement `OrchestratorAgent` that coordinates the five specialized agents in sequence.

**Behaviour:**
- Accepts `OrchestrationRequest(ProjectBrief, Context?, MaxIterationsPerAgent)`
- Dispatches an `AgentTask` to each agent in order: PO → PM → BA → DEV → TEST
- Each next agent receives the accumulated context from all previous agents
- Collects `AgentTaskResult` from each agent
- Generates a `Summary` string that stitches together the key outputs
- Returns `OrchestrationResult(Summary, AgentOutputs)`

**Done when:** `OrchestratorAgent` passes all unit tests.

---

### Phase 4 — API Endpoint *(Exposure)*

**Goal:** Expose the orchestrator as a REST endpoint.

| Property | Value |
|---|---|
| Verb | `POST` |
| Route | `/api/orchestrator/run` |
| Request body | `OrchestrationRequest` |
| Response body | `OrchestrationResult` |
| Validation | `ProjectBrief` non-empty; `MaxIterationsPerAgent` 1–50 |

**Done when:** Endpoint returns 200 with a fully populated `OrchestrationResult`.

---

### Phase 5 — Tests *(Quality Gate)*

**Goal:** Every new type has at least one meaningful xUnit test.

| Test | Verifies |
|---|---|
| `Orchestrator_RunsAllFiveAgents` | `AgentOutputs.Count == 5` |
| `Orchestrator_ContextIsForwarded` | Each agent output contains context from predecessor |
| `Orchestrator_EmptyBrief_Throws` | `ArgumentException` on empty brief |
| `PO_OutputContainsBrief` | PO output references the project brief |
| `PM_OutputContainsMilestones` | PM output contains milestone keywords |
| `BA_OutputContainsRequirements` | BA output contains requirement keywords |
| `DEV_OutputContainsArchitecture` | DEV output contains tech/architecture keywords |
| `TEST_OutputContainsTestPlan` | TEST output contains test plan keywords |

**Done when:** `dotnet test` exits 0.

---

### Phase 6 — Documentation *(Compliance)*

**Goal:** Keep `docs/technical.md` and `docs/business.md` in sync with the new implementation.

| Update | Target file |
|---|---|
| New interfaces (`ISpecializedAgent`, `IOrchestratorAgent`) | `technical.md` → Interfaces section |
| New models (`AgentTask`, `AgentTaskResult`, `OrchestrationRequest`, `OrchestrationResult`) | `technical.md` → Data Models section |
| New agents table | `technical.md` → Built-in Tools section |
| New API endpoint | `technical.md` → API Reference section |
| DI registrations | `technical.md` → Dependency Injection section |
| New tests | `technical.md` → Test Coverage table |
| Business-level orchestration description | `business.md` → Key Capabilities and How to Request a Plan sections |

---

## Technology Constraints

- No external LLM API calls — all agents are rule-based for now. An LLM adapter can be injected later via the `ISpecializedAgent` interface.
- No new NuGet packages required for the core implementation.
- All agents must be registered with DI via `AddScoped<ISpecializedAgent, T>()`.
- `OrchestratorAgent` must be registered as `AddScoped<IOrchestratorAgent, OrchestratorAgent>()`.

---

## Future Enhancements (Out of Scope for This Roadmap)

| Enhancement | Description |
|---|---|
| LLM-backed agents | Replace rule-based `ExecuteAsync` with an LLM call (OpenAI, Azure OpenAI, Ollama) |
| Parallel agent dispatch | Run independent agents (DEV + TEST) in parallel with `Task.WhenAll` |
| Agent-to-agent feedback loops | Allow TEST to send feedback back to DEV for revision |
| Persistent agent memory | Store `OrchestrationResult` in a database per project |
| Agent specialization by domain | Spin up domain-specific sub-agents (e.g., mobile DEV vs. backend DEV) |
| Human-in-the-loop approval | Pause at each phase for stakeholder sign-off before continuing |
