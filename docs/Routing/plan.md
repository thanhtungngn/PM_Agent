# PM Agent - Routing Implementation Plan

> Audience: Developers and architects.
> Related docs: [../technical.md](../technical.md), [../roadmap.md](../roadmap.md), [../business.md](../business.md)

---

## Purpose

Define a practical, phased implementation plan for moving from static sequential dispatch (PO -> PM -> BA -> DEV -> TEST) to dynamic routing with measurable quality, latency, and cost improvements.

## Scope

In scope:
- Dynamic agent routing policy
- Standardized agent output schema for routing decisions
- Observability for routing quality and cost
- Controlled rollout with feature flags and rollback

Out of scope:
- Full long-term memory (RAG)
- Agent debate mode
- Event-driven or broker-based orchestration

---

## Current Baseline

Current behavior:
- Every request runs all agents in a fixed sequence.
- No conditional skip or early-stop behavior.
- Routing decisions are not confidence-aware.
- Routing outcomes are not measured by dedicated routing KPIs.

Current risks:
- Unnecessary LLM calls for simple requests.
- Increased p95 latency.
- Cost growth that is not bounded by request complexity.

---

## Target Routing Model

### Routing goals

1. Run only the minimum required agents for each request type.
2. Keep quality at or above current baseline while reducing cost and latency.
3. Make routing behavior observable, tunable, and safe to roll back.

### Decision strategy

The router determines the next agent from:
- request intent category (planning, architecture, test strategy, mixed)
- complexity score (low, medium, high)
- missing information severity
- prior agent output confidence and issue flags

### Route control rules

- Start with a required initial role set (usually PO or PM depending on intent).
- Skip optional roles when confidence is above threshold and no blockers exist.
- Early-stop when acceptance conditions are met.
- Fallback to full chain when confidence is low or validation fails.

---

## Standard Output Contract (Routing-Critical)

Each specialized agent output must include:

```json
{
  "role": "PO|PM|BA|DEV|TEST",
  "decision": "continue|stop|escalate",
  "confidence": 0.0,
  "issues": ["..."],
  "next_action": "...",
  "content": "..."
}
```

Contract notes:
- confidence range: 0.0 to 1.0
- issues must be explicit blockers or quality concerns
- next_action is mandatory to support deterministic routing transitions

---

## Implementation Phases

## Phase 1 - Schema and Guardrails

Deliverables:
- Add/confirm structured output fields used by routing logic.
- Add schema validation in orchestrator path.
- Define static confidence thresholds per decision type.

Definition of done:
- All five agents produce parseable structured responses.
- Orchestrator rejects malformed responses with safe fallback.

Dependencies:
- None

## Phase 2 - Router Policy Engine

Deliverables:
- Introduce router policy abstraction (rules first, model-optional later).
- Add first-pass routing matrix by intent and complexity.
- Implement skip/early-stop/fallback transitions.

Definition of done:
- Router can produce a role sequence before execution and adjust during execution.
- Full-chain fallback is always available.

Dependencies:
- Phase 1

## Phase 3 - Orchestrator Integration

Deliverables:
- Integrate routing policy into orchestration loop.
- Ensure context propagation still works with skipped roles.
- Preserve deterministic behavior in test mode.

Definition of done:
- Orchestrator supports dynamic role order and dynamic role subset.
- Existing API contract remains backward-compatible.

Dependencies:
- Phase 2

## Phase 4 - Observability and Cost Controls

Deliverables:
- Emit per-request routing telemetry:
  - selected roles
  - skipped roles
  - confidence trajectory
  - token usage
  - latency per agent
  - estimated cost
- Add response caching and early-stop metrics.

Definition of done:
- Dashboards or logs can explain why each role was/was not selected.
- Cost and latency can be compared against baseline.

Dependencies:
- Phase 3

## Phase 5 - Controlled Rollout

Deliverables:
- Add feature flags for dynamic routing.
- Roll out in stages (internal -> limited -> default-on).
- Define and test rollback criteria.

Definition of done:
- Dynamic routing can be disabled instantly to restore full sequential flow.
- Rollout gates are based on KPI thresholds.

Dependencies:
- Phase 4

---

## KPI Targets

Initial targets after rollout:
- Reduce median agent calls per request by >= 30%
- Reduce p95 latency by >= 20%
- Keep quality acceptance rate within -5% of baseline or better
- Reduce estimated cost per request by >= 25%

---

## Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Misrouting skips needed role | Quality regression | fallback to full chain, conservative thresholds |
| Confidence inflation | Incorrect early-stop | cap confidence by validation checks |
| Latency spikes from retries | SLA risk | max retry limits, timeout-aware routing |
| Schema drift between agents | Router failures | strict schema validation and contract tests |

---

## Verification Matrix

| Level | Validation |
|---|---|
| Unit | Router chooses expected role set per scenario |
| Integration | Orchestrator honors dynamic routes and fallback |
| Contract | Agent output schema parse/validate tests |
| Runtime | Telemetry confirms route decision traceability |

---

## Start Implementation Backlog

1. Add structured routing fields to agent outputs.
2. Add router policy interface and baseline rules.
3. Integrate dynamic role selection in orchestrator.
4. Add telemetry fields required for routing diagnostics.
5. Add unit and integration tests for route decisions and fallback.
6. Enable feature flag and validate staged rollout.
