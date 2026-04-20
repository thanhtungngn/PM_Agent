# PM Agent — Migration to Agent-Based Architecture

> **Audience:** Developers, architects, and technical leads who need to understand what changed between the original rule-based implementation and the current Agent-Based architecture, why it changed, and what the practical impact is.

---

## Overview

This document records the full upgrade from the original **Rule-Based Architecture (v1)** to the **Agent-Based Architecture (v2)**. It covers:

- A side-by-side comparison of every major architectural area
- Detailed improvement notes per component
- Hiring workflow evolution (v1 → v2)
- New interfaces, models, and DI registrations introduced
- What stayed the same
- Migration notes for contributors

---

## Version Summary

| Version | Label | Key characteristic |
|---|---|---|
| **v1** | Rule-Based Architecture | Hard-coded logic, static templates, no LLM calls in the core agent loop, fixed sequential dispatch |
| **v2** | Agent-Based Architecture | LLM-first reasoning at every layer, `IAgentMemory` for context accumulation, dynamic routing, multi-role hiring workflow, quality harness |

---

## 1. Agent Loop (`AgentExecutor`)

### v1 — Rule-Based Loop

The executor ran a hard-coded tool sequence on every request:

```
scope_analysis → risk_assessment → action_planner → finalize
```

There was no reasoning step. The Think step was entirely deterministic — a sequence of `if (!usedActions.Contains(...))` guards. No LLM was involved:

```csharp
// v1 ThinkRuleBased — the only Think path
if (!usedActions.Contains("scope_analysis"))
    return ("scope_analysis", goal, false);
if (!usedActions.Contains("risk_assessment"))
    return ("risk_assessment", context, false);
if (!usedActions.Contains("action_planner"))
    return ("action_planner", context, false);
return ("finalize", context, true);
```

Tools themselves also returned static strings based on `string.Contains` keyword matching rather than LLM reasoning.

### v2 — ReAct Loop with LLM-driven Think

The executor now follows the **ReAct (Reasoning + Acting)** pattern:

```
THINK (LLM) → ACTION → INPUT → OUTPUT → record to IAgentMemory → THINK (LLM) → …
```

The Think step calls `ILlmClient` with:
- the goal
- all completed step history
- accumulated `IAgentMemory` context

The LLM responds with structured JSON:

```json
// To invoke a tool
{"thought": "My reasoning", "action": "scope_analysis", "actionInput": "..."}

// To finalize
{"thought": "My reasoning", "isFinal": true}
```

If the LLM is unavailable or returns non-JSON, the executor automatically falls back to the rule-based sequence from v1. This fallback ensures the system remains operational in offline or low-availability environments.

Each tool now calls `ILlmClient` internally with a domain-specific system prompt, producing context-aware output instead of keyword-based static strings.

### Comparison

| Aspect | v1 | v2 |
|---|---|---|
| Think step | Hard-coded sequence | LLM-driven JSON decision |
| Tool execution | Static templates / keyword matching | LLM call with focused system prompt |
| Context accumulation | Manual string concatenation | `IAgentMemory` — structured append-only log |
| Fallback | Not applicable (always rule-based) | Automatic rule-based fallback when LLM unavailable |
| Output quality | Predictable but generic | Context-aware, goal-specific output |
| Testability | Deterministic — easy to assert exact strings | Requires `FakeEchoLlmClient` or mock for tests |

---

## 2. Orchestrator (`OrchestratorAgent`)

### v1 — Static Sequential Dispatch

The orchestrator dispatched all agents in a fixed order on every request:

```
PO → PM → BA → DEV → TEST
```

There was no conditional logic, no early-stop, and no role skipping. Every request paid the full cost of all five agents regardless of what the request actually needed.

Initial agent count: **5 roles** (PO, PM, BA, DEV, TEST).

### v2 — LLM-driven Dynamic Routing

After each agent completes, the orchestrator now asks `ILlmClient`:

```json
// LLM routing response
{"decision": "continue", "nextRole": "BA", "reasoning": "..."}
{"decision": "stop",     "reasoning": "Deliverable is complete"}
{"decision": "escalate", "reasoning": "Low confidence — need full chain"}
```

This enables:
- **Intent-based initial routing** — a planning request uses `PO → PM → BA`; a build request uses `PO → BA → DEV → TEST`; a hiring request uses `PM → HR → BA`
- **Early-stop** when the LLM is confident the deliverable is complete
- **Escalation** to full chain when quality is low or confidence drops
- **Skip** of roles that are not needed for the current request

Rule-based fallback (`RuleBasedAgentRoutingPolicy`) is still available and activates automatically when the LLM is unavailable or returns non-parseable routing JSON.

Agent count expanded to **7 roles**: PO, PM, HR, BA, DEV, TEST, HIRING_ORC.

### Comparison

| Aspect | v1 | v2 |
|---|---|---|
| Dispatch order | Fixed: PO → PM → BA → DEV → TEST | Dynamic: LLM-driven per-request routing |
| Agent count | 5 | 7 (+ HR, HIRING_ORC) |
| Early-stop | Not supported | LLM or rule-based early-stop |
| Role skipping | Not supported | Supported — only relevant roles run |
| Hiring support | Not supported | Dedicated hiring route + `HiringOrchestrationAgent` |
| Routing fallback | Not applicable | `RuleBasedAgentRoutingPolicy` auto-fallback |
| LLM requirement | None | Optional — falls back to rule-based without LLM |

---

## 3. Specialized Agents

### v1 — Rule-Based Agents

All five agents were fully rule-based. Each `ExecuteAsync` method built output from static strings, inline conditionals, and keyword checks:

```csharp
// v1 ProductOwnerAgent (example shape)
return $"## Product Owner Output\n\n**Goal:** {task.Goal}\n\n**User Stories:**\n- As a user, I want...";
```

No LLM was called. Output was deterministic and structurally identical on every request regardless of the actual project brief content.

### v2 — LLM-Backed Agents via `SpecializedAgentBase`

A shared base class `SpecializedAgentBase` now centralizes all shared logic:

```csharp
public abstract class SpecializedAgentBase : ISpecializedAgent
{
    protected SpecializedAgentBase(ILlmClient llm, ILogger logger) { ... }
    protected abstract string SystemPrompt { get; }
    // ExecuteAsync → calls ILlmClient.CompleteAsync(SystemPrompt, BuildUserMessage(task))
}
```

Each concrete agent only declares:
- `Role` (token string)
- `Description`
- `SystemPrompt` (role-specific LLM instruction)

This means adding a new agent is now a ~15-line class definition with no boilerplate.

`HiringOrchestrationAgent` uses its own `IAgentMemory` instance to accumulate context across reasoning phases — it does not extend `SpecializedAgentBase` because it requires full memory access.

### Comparison

| Aspect | v1 | v2 |
|---|---|---|
| Implementation pattern | Per-agent rule-based string building | Shared `SpecializedAgentBase` with LLM call |
| Output quality | Static, template-like | Context-aware, brief-specific |
| Adding a new agent | ~50–100 lines of logic | ~15 lines (Role + Description + SystemPrompt) |
| Context forwarding | String accumulation passed in `task.Context` | Same pattern — context still passed via `AgentTask.Context` |
| Routing metadata | Not present | Each result includes `decision`, `confidence`, `issues`, `nextAction` |
| Hiring orchestration | Not present | `HiringOrchestrationAgent` (`HIRING_ORC`) |

---

## 4. Planning Layer (`IAgentPlanner`)

### v1 — `RuleBasedAgentPlanner`

The only implementation of `IAgentPlanner` was purely rule-based:

```csharp
// v1 — always returned the same template structure
nextActions = ["Clarify acceptance criteria...", "Break work into weekly milestones...", ...]
risks = ["Scope creep without strict change control.", "Hidden dependencies..."]
```

Output was independent of the actual `ProjectName`, `Goal`, `TeamMembers`, and `Constraints`. The plan was effectively a fixed template with minor string interpolation.

### v2 — `LlmAgentPlanner`

`LlmAgentPlanner` replaces `RuleBasedAgentPlanner` as the default DI registration. It sends the full planning request to the LLM and expects structured JSON output:

```json
{
  "summary": "...",
  "nextActions": ["..."],
  "risks": ["... — mitigation hint"]
}
```

`RuleBasedAgentPlanner` still exists as a backward-compatible fallback (used by legacy tests), but is no longer registered in the DI container by default.

### Comparison

| Aspect | v1 | v2 |
|---|---|---|
| Implementation | `RuleBasedAgentPlanner` | `LlmAgentPlanner` |
| Output | Fixed templates | Context-aware, LLM-generated |
| Fallback | Not applicable | Inline fallback to rule-based response when LLM unavailable |
| DI registration | `AddScoped<IAgentPlanner, RuleBasedAgentPlanner>()` | `AddScoped<IAgentPlanner, LlmAgentPlanner>()` |

---

## 5. Agent Memory (`IAgentMemory`)

### v1 — Manual String Concatenation

Context was passed between steps and agents using raw string concatenation:

```csharp
// v1 — context built manually on each iteration
accumulatedContext = $"{accumulatedContext}\n\n[{role}]:\n{result.Output}";
```

This was fragile — there was no structure, no timestamps, no role labels, and no easy way to extract individual entries.

### v2 — `IAgentMemory` / `InMemoryAgentMemory`

A dedicated `IAgentMemory` abstraction replaces all manual concatenation:

```csharp
public interface IAgentMemory
{
    void Record(string role, string content);
    string BuildContext();                        // formatted [ROLE]:\ncontent blocks
    IReadOnlyList<MemoryEntry> Entries { get; }  // structured access to all entries
}
```

`AgentExecutor` records each tool output to memory after every step. The accumulated context is passed into the next Think step through `memory.BuildContext()`. `HiringOrchestrationAgent` uses its own memory instance to accumulate hiring context (`PROJECT_BRIEF`, `HIRING_CONTEXT`, `HIRING_ORC` output) so downstream agents can reference the full hiring assessment without re-processing raw JD/CV text.

Memory is registered as `Transient` in DI so each component that injects it receives its own fresh instance.

### Comparison

| Aspect | v1 | v2 |
|---|---|---|
| Context mechanism | Manual string concat | `IAgentMemory` append-only log |
| Structure | Unstructured string | `List<MemoryEntry(Role, Content, RecordedAt)>` |
| Accessibility | Not queryable | `Entries` property — full structured access |
| Leakage prevention | Not applicable | Transient DI registration — fresh instance per component |

---

## 6. Hiring Workflow

This was the most significant feature addition in v2. There was no hiring workflow in v1.

### v1 — No Hiring Support

v1 had no hiring-related functionality. The orchestrator supported only the delivery workflow (PO → PM → BA → DEV → TEST). There was no HR agent, no CV screening, no interview flow, and no candidate artifacts.

### v2 — Two Complementary Hiring Paths

#### Path A: One-Shot Hiring Orchestration (`HiringOrchestrationAgent`)

A new specialized agent (`HIRING_ORC`) was introduced that performs a complete end-to-end hiring assessment in a single LLM-driven pass:

| Section | Content |
|---|---|
| CV Analysis | Skills, experience, notable projects, strengths and gaps |
| JD Fit Assessment | Per-requirement rating: Strong Match / Partial Match / Gap |
| Screening Decision | Proceed / Hold / Reject + confidence % + rationale |
| Interview Plan | Stages, panel, estimated duration, sample questions |
| Final Recommendation | 2–3 sentence synthesis with concrete next steps |

Uses `IAgentMemory` to accumulate context across reasoning phases. Invoked through `POST /api/orchestrator/run` with `workflow=hiring`.

#### Path B: Staged Interactive Interview (`IHiringWorkflowService`)

A full approval-gated multi-turn interview workflow:

| Stage | Description |
|---|---|
| `awaiting_screening_approval` | HR semantic fit scoring done; waiting for human approval to forward CV |
| `awaiting_interview_approval` | PM prepared schedule; waiting for approval to start (optional) |
| `interview_active` | Live panel interview with language lock, scoring, hints, and follow-ups |
| `completed` | Session closed normally |
| `rejected` | Candidate rejected at screening or by user decision |

Key capabilities introduced:

- **Semantic HR screening** — `LlmHiringFitScoringAgent` evaluates CV/JD fit holistically rather than matching keywords. Returns score, strengths, gaps, and advance/reject decision.
- **Interview language lock** — HR asks the candidate to choose English or Vietnamese before any technical questions. The locked language applies to all generated questions, fallback templates, clarification replies, and feedback turns.
- **LLM one-shot question bank** — `ConfigurableInterviewQuestionProvider` generates the full question bank once after language selection, biased toward JD stack priorities and candidate overlap across five categories: programming languages, frameworks, system design, databases, and agile/scrum methodology.
- **Seniority calibration** — `HiringSeniorityResolver` resolves `JUNIOR`, `MID`, or `SENIOR` from the request or inferred from hiring materials. This propagates to screening, question generation, and scoring.
- **Per-answer LLM scoring** — `LlmInterviewScoringAgent` evaluates each accepted answer individually using a configurable dimension rubric (`communication`, `problem_solving`, `technical_judgment`, `ownership`, `collaboration`). The session score is a running average of answer scores.
- **Candidate turn classification** — each candidate message is classified as hint request, clarification/side question, or accepted answer before routing. Non-answer turns are never scored.
- **Interviewer feedback loop** — after each accepted answer the active interviewer adds a conversational feedback turn, making the session feel like a real interview rather than a silent scoring event.
- **Follow-up question gate** — follow-up questions are only asked when the candidate showed solid understanding (answer quality gate), preventing trivial follow-ups after weak answers.
- **Clarification guardrails** — if a candidate asks too many side questions on the same active question, the interviewer adds a focus reminder and explicitly returns the candidate to the active question.
- **Hint delivery** — `POST /hint` or `IsHintRequest = true` returns 2–3 keyword prompts without advancing the question.
- **Per-candidate artifact folder** — `candidate-{sessionId}/` is created at session start and holds `jd-keywords.md`, `cv-keywords.md`, `interview-qa.md` (live Q&A log), and `hiring-session-{sessionId}.md` (full markdown notes snapshot).
- **HrAgent** — new specialized agent role `HR` added to the delivery orchestrator for hiring plan and staffing strategy output.

### Hiring Workflow Comparison

| Feature | v1 | v2 |
|---|---|---|
| HR agent in orchestrator | ✗ | ✓ (`HrAgent`, role `HR`) |
| One-shot hiring assessment | ✗ | ✓ (`HiringOrchestrationAgent`, role `HIRING_ORC`) |
| Staged interactive interview | ✗ | ✓ (`IHiringWorkflowService`) |
| CV screening | ✗ | ✓ Semantic LLM fit scoring with strengths/gaps |
| Interview language lock | ✗ | ✓ EN or VI, locked before main questions |
| LLM-generated questions | ✗ | ✓ One-shot bank after language selection |
| Seniority calibration | ✗ | ✓ AUTO / JUNIOR / MID / SENIOR |
| Per-answer scoring | ✗ | ✓ LLM dimension rubric, running average |
| Follow-up question gate | ✗ | ✓ Quality-gated before asking |
| Hint delivery | ✗ | ✓ Keyword hints without advancing question |
| Clarification guardrails | ✗ | ✓ Focus reminder after threshold |
| Candidate folder artifacts | ✗ | ✓ `jd-keywords.md`, `cv-keywords.md`, `interview-qa.md` |
| Interview notes file | ✗ | ✓ `hiring-session-{sessionId}.md` |

---

## 7. LLM Provider Layer

### v1 — No LLM Integration

v1 had no `ILlmClient` abstraction and no LLM calls anywhere in the codebase. All logic was deterministic.

### v2 — `ILlmClient` with Provider Selection

A provider-agnostic `ILlmClient` abstraction:

```csharp
Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct);
```

Two concrete implementations, selected at startup from `LlmSettings:Provider`:

| Provider | Class | When to use |
|---|---|---|
| `OpenAI` (default) | `OpenAiLlmClient` | Production — requires `ApiKey` |
| `Ollama` | `OllamaLlmClient` | Local development — zero cost, runs on local hardware |

All agent components (`AgentExecutor`, `OrchestratorAgent`, tools, specialized agents, scoring agents, question provider) share the same `ILlmClient` instance resolved from DI.

The Ollama health check (`OllamaHealthCheck`) is conditionally registered when `Provider = Ollama`.

---

## 8. Quality Harness (New in v2)

There was no quality harness in v1. v2 introduces a dedicated harness layer for deterministic scenario-based validation without requiring a live LLM.

### Components

| Component | Responsibility |
|---|---|
| `IHarnessScenarioProvider` | Returns the catalog of named test scenarios |
| `IHarnessRunner` | Drives the agent system through all or a single scenario |
| `IHarnessAssertionEngine` | Evaluates output against expected sections, decision, and confidence |
| `IHarnessReportSink` | Persists `HarnessReport` to disk (JSON + Markdown sinks) |

### Built-in Scenarios (7)

| Scenario ID | What it validates |
|---|---|
| `delivery-happy` | Full delivery chain with expected output sections |
| `delivery-ambiguous` | Vague brief still produces decision and confidence |
| `delivery-edge` | Minimal one-word brief — graceful handling |
| `fault-empty-llm` | Empty LLM response triggers assertion failure correctly |
| `fault-llm-exception` | LLM exception is caught and scenario marked Pass |
| `hiring-happy` | Matching CV produces full interview pack |
| `hiring-below-threshold` | Low-fit CV produces rejection result |

### CI Integration

Two CI jobs were added:

| Job | Filter | LLM needed | Fail condition |
|---|---|---|---|
| `fast` | `Category=Harness` | No | Any test failure |
| `nightly` | `Category=HarnessLLM` | Yes (`OPENAI_API_KEY`) | Any failure or pass rate < 95% |

---

## 9. New Interfaces and Models

All new items were added to `PMAgent.Application` (contracts only — no implementation in this layer):

### New Interfaces

| Interface | Layer | Responsibility |
|---|---|---|
| `ILlmClient` | Application/Abstractions | Provider-agnostic LLM call abstraction |
| `IAgentMemory` | Application/Abstractions | Append-only context log for agent runs |
| `ISpecializedAgent` | Application/Abstractions | Contract for all role-specific agents |
| `IOrchestratorAgent` | Application/Abstractions | Contract for the multi-agent coordinator |
| `IAgentRoutingPolicy` | Application/Abstractions | Routing matrix and transition guards |
| `IHiringWorkflowService` | Application/Abstractions | Staged hiring session contract |
| `IHiringFitScoringAgent` | Application/Abstractions | Semantic CV/JD fit scoring |
| `IInterviewScoringAgent` | Application/Abstractions | Per-answer interview evaluation |
| `IInterviewQuestionProvider` | Application/Abstractions | One-shot question bank + runtime reply generation |
| `IHarnessScenarioProvider` | Application/Abstractions/Harness | Test scenario catalog |
| `IHarnessRunner` | Application/Abstractions/Harness | Scenario execution driver |
| `IHarnessAssertionEngine` | Application/Abstractions/Harness | Output assertion evaluator |
| `IHarnessReportSink` | Application/Abstractions/Harness | Report persistence (multiple sinks) |

### New Models (selected)

| Model | Added in v2 | Purpose |
|---|---|---|
| `AgentTask` | ✓ | Input to a specialized agent (role, goal, context) |
| `AgentTaskResult` | ✓ | Output from a specialized agent including routing metadata |
| `OrchestrationRequest` | ✓ | Input to the orchestrator (project brief, workflow, JD, CV) |
| `OrchestrationResult` | ✓ | Aggregated output from all agents plus summary |
| `MemoryEntry` | ✓ | Single entry in `IAgentMemory` (role, content, timestamp) |
| `HiringSessionStartRequest` | ✓ | Input to start a staged hiring session |
| `HiringSessionResult` | ✓ | Full session state snapshot |
| `HiringApprovalRequest` | ✓ | Approval/rejection decision with optional comment |
| `HiringCandidateResponseRequest` | ✓ | Candidate turn submission (message + hint flag) |
| `HiringFitAssessmentResult` | ✓ | HR screening score, decision, strengths, gaps |
| `InterviewQuestion` | ✓ | A single interview question with follow-up and hint keywords |
| `InterviewScoreResult` | ✓ | Per-answer score, dimensions, feedback, follow-up gate |
| `HiringWorkflowSettings` | ✓ | Configuration object for screening and scoring thresholds |
| `InterviewScoringSettings` | ✓ | Dimension rubric, seniority profiles, early-stop thresholds |
| `HarnessScenario` | ✓ | A named test case with expected sections and fault flags |
| `HarnessReport` | ✓ | Aggregated harness run result with pass rate |
| `LlmSettings` | ✓ | Provider selection and credentials configuration |

---

## 10. Dependency Injection Changes

### v1 DI (simplified)

```csharp
services.AddScoped<IAgentPlanner, RuleBasedAgentPlanner>();
services.AddScoped<IAgentTool, ScopeAnalysisTool>();
services.AddScoped<IAgentTool, RiskAssessmentTool>();
services.AddScoped<IAgentTool, ActionPlannerTool>();
services.AddScoped<IAgentExecutor, AgentExecutor>();
```

No LLM client, no specialized agents, no orchestrator, no hiring workflow, no harness.

### v2 DI (current)

```csharp
// LLM client — provider selected from LlmSettings:Provider
services.AddScoped<ILlmClient, OpenAiLlmClient>();  // or OllamaLlmClient

// Planner
services.AddScoped<IAgentPlanner, LlmAgentPlanner>();
services.AddScoped<IAgentRoutingPolicy, RuleBasedAgentRoutingPolicy>();

// Hiring
services.AddSingleton(hiringWorkflowSettings);
services.AddScoped<IHiringFitScoringAgent, LlmHiringFitScoringAgent>();
services.AddScoped<IInterviewQuestionProvider, ConfigurableInterviewQuestionProvider>();
services.AddScoped<IInterviewScoringAgent, LlmInterviewScoringAgent>();
services.AddScoped<IHiringWorkflowService, InMemoryHiringWorkflowService>();

// Agent memory
services.AddTransient<IAgentMemory, InMemoryAgentMemory>();

// Tools
services.AddScoped<IAgentTool, ScopeAnalysisTool>();
services.AddScoped<IAgentTool, RiskAssessmentTool>();
services.AddScoped<IAgentTool, ActionPlannerTool>();

// Agent loop
services.AddScoped<IAgentExecutor, AgentExecutor>();

// Specialized agents
services.AddScoped<ISpecializedAgent, ProductOwnerAgent>();
services.AddScoped<ISpecializedAgent, ProjectManagerAgent>();
services.AddScoped<ISpecializedAgent, HrAgent>();
services.AddScoped<ISpecializedAgent, BusinessAnalystAgent>();
services.AddScoped<ISpecializedAgent, DeveloperAgent>();
services.AddScoped<ISpecializedAgent, TesterAgent>();
services.AddScoped<ISpecializedAgent, HiringOrchestrationAgent>();

// Orchestrator
services.AddScoped<IOrchestratorAgent, OrchestratorAgent>();

// Harness
services.AddSingleton<IHarnessScenarioProvider, DefaultHarnessScenarioProvider>();
services.AddScoped<IHarnessAssertionEngine, HarnessAssertionEngine>();
services.AddScoped<IHarnessReportSink, JsonHarnessReportSink>();
services.AddScoped<IHarnessReportSink, MarkdownHarnessReportSink>();
services.AddScoped<IHarnessRunner, HarnessRunner>();
```

Key DI design decisions:
- `IAgentMemory` is `Transient` — each component gets a fresh instance, preventing state leakage between requests.
- `HiringWorkflowSettings` is `Singleton` — configuration is loaded once and shared.
- All `IAgentTool` and `ISpecializedAgent` registrations are resolved as `IEnumerable<T>` by `AgentExecutor` and `OrchestratorAgent` respectively, so adding a new tool or agent requires only one `AddScoped` line.

---

## 11. What Did Not Change

The following items were preserved without structural changes:

| Item | Status |
|---|---|
| `AgentStep` model | Unchanged |
| `AgentRunRequest` / `AgentRunResult` | Unchanged |
| `POST /api/agent/run` endpoint | Unchanged |
| Three built-in tool names (`scope_analysis`, `risk_assessment`, `action_planner`) | Unchanged |
| `IAgentTool` contract | Unchanged |
| `IAgentExecutor` contract | Unchanged |
| `IAgentPlanner` contract | Unchanged |
| `RuleBasedAgentPlanner` | Still exists (legacy tests) |
| `ThinkRuleBased` inside `AgentExecutor` | Still exists as automatic fallback |
| Solution layer structure (Api / Application / Infrastructure / Domain) | Unchanged |
| Clean Architecture layering rules | Unchanged |

---

## 12. Breaking Changes

| Area | Change | Impact |
|---|---|---|
| Default `IAgentPlanner` | Switched from `RuleBasedAgentPlanner` to `LlmAgentPlanner` | Planning responses are now LLM-generated; tests relying on exact static strings will fail |
| Specialized agent outputs | Now LLM-generated via `ILlmClient` | Agent outputs are no longer deterministic; tests must use `FakeEchoLlmClient` or mocks |
| Tool outputs | Now LLM-generated via `ILlmClient` | Tool outputs are no longer deterministic |
| `AgentExecutor` constructor | Added optional `ILlmClient? llm = null` parameter | Backward compatible — existing DI wiring still works |
| `OrchestratorAgent` constructor | Added optional `ILlmClient? llm = null` parameter | Backward compatible |
| Orchestrator dispatch order | Now LLM-driven — not guaranteed to follow fixed PO→PM→BA→DEV→TEST order | Integration tests depending on exact agent order may be affected |

---

## 13. Migration Notes for Contributors

### Running Without an LLM

The system is fully operational without any LLM configured. Both `AgentExecutor` and `OrchestratorAgent` detect `ILlmClient = null` and fall back to rule-based behavior automatically. The harness CI job (`Category=Harness`) runs entirely without an LLM.

### Local Development with Ollama

```bash
# 1. Install Ollama → https://ollama.com/download
# 2. Pull a model
ollama pull llama3.2
# 3. Run with Development profile (automatically uses Ollama)
dotnet run --project src/PMAgent.Api --launch-profile Development
```

### Writing Tests for LLM-Backed Components

Use `FakeEchoLlmClient` (available in the test project) to inject a controllable LLM response. Tests should not depend on the exact content of LLM outputs — only on structural invariants (e.g., `IsFinal == true`, `Steps.Count >= 1`, `FinalAnswer` contains the goal string).

### Adding a New Specialized Agent

1. Create a class in `src/PMAgent.Infrastructure/Agents/` that extends `SpecializedAgentBase`.
2. Declare `Role`, `Description`, and `SystemPrompt`.
3. Register: `services.AddScoped<ISpecializedAgent, YourAgent>()` in `DependencyInjection.cs`.
4. Update `docs/technical.md` — Specialized Agents table.
5. Update `docs/business.md` — Key Capabilities and Orchestrator table.

### Configuring a Different LLM Model

Edit `appsettings.json` (production) or `appsettings.Development.json` (local):

```json
"LlmSettings": {
  "Provider": "OpenAI",
  "ApiKey":   "<your-key>",
  "Model":    "gpt-4o"
}
```

---

## 14. Architecture Diagram Comparison

### v1 Architecture

```
Client
  |
  v
AgentController
  |
  v
AgentExecutor  ←── fixed loop: scope → risk → action_planner → finalize
  |
  ├── ScopeAnalysisTool     (rule-based string)
  ├── RiskAssessmentTool    (rule-based string)
  └── ActionPlannerTool     (rule-based string)

PlanningController
  |
  v
RuleBasedAgentPlanner       (static templates)
```

### v2 Architecture

```
Client
  |
  ├── AgentController          POST /api/agent/run
  │     └── AgentExecutor  ←── ReAct loop (LLM Think + IAgentMemory)
  │           ├── ScopeAnalysisTool     (ILlmClient)
  │           ├── RiskAssessmentTool    (ILlmClient)
  │           └── ActionPlannerTool     (ILlmClient)
  │
  ├── OrchestratorController   POST /api/orchestrator/run
  │     └── OrchestratorAgent  ←── LLM-driven routing + IAgentRoutingPolicy fallback
  │           ├── ProductOwnerAgent       (ILlmClient via SpecializedAgentBase)
  │           ├── ProjectManagerAgent     (ILlmClient via SpecializedAgentBase)
  │           ├── HrAgent                 (ILlmClient via SpecializedAgentBase)
  │           ├── BusinessAnalystAgent    (ILlmClient via SpecializedAgentBase)
  │           ├── DeveloperAgent          (ILlmClient via SpecializedAgentBase)
  │           ├── TesterAgent             (ILlmClient via SpecializedAgentBase)
  │           └── HiringOrchestrationAgent (ILlmClient + IAgentMemory)
  │
  ├── HiringWorkflowController POST /api/hiring/sessions
  │     └── InMemoryHiringWorkflowService
  │           ├── LlmHiringFitScoringAgent
  │           ├── ConfigurableInterviewQuestionProvider
  │           ├── LlmInterviewScoringAgent
  │           └── candidate-{sessionId}/  (artifacts)
  │
  ├── PlanningController       POST /api/planning/next-actions  (legacy)
  │     └── LlmAgentPlanner    (ILlmClient + fallback)
  │
  └── HarnessController        POST /api/harness/run
        └── HarnessRunner
              ├── DefaultHarnessScenarioProvider
              ├── HarnessAssertionEngine
              ├── JsonHarnessReportSink
              └── MarkdownHarnessReportSink
```

---

## 15. Related Documents

| Document | Path | Purpose |
|---|---|---|
| Technical documentation | `docs/technical.md` | Full API reference, interfaces, DI, test coverage |
| Business documentation | `docs/business.md` | Capabilities, glossary, stakeholder guide |
| Hiring workflow architecture | `docs/hiring-workflow.md` | Deep-dive into hiring process design, trade-offs |
| Hiring workflow business summary | `docs/hiring-workflow-business-summary.md` | Short stakeholder-facing hiring guide |
| Orchestrator roadmap | `docs/roadmap.md` | Original multi-agent vision and phased plan |
| Routing implementation plan | `docs/Routing/plan.md` | Dynamic routing design, KPIs, phased rollout |
