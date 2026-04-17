# PM Agent — Business Documentation

> **Audience:** Product owners, business analysts, and project managers.
> For technical implementation details, see [technical.md](technical.md).

---

## What Is PM Agent?

PM Agent is an AI-powered planning assistant for project managers. Given a project goal, it automatically analyses the scope, identifies key risks, and produces a concrete action plan — all in a single API call.

It replaces the manual effort of running three separate planning sessions (scope review → risk workshop → action planning) with an automated reasoning loop that completes the same work in seconds.

---

## The Problem It Solves

| Without PM Agent | With PM Agent |
|---|---|
| Scope, risks, and plans live in separate documents | One unified response covers all three |
| Planning depends on the availability of the right people | Available on-demand, 24/7 |
| No audit trail of how a plan was derived | Every reasoning step is recorded and visible |
| Inconsistent planning quality across projects | Consistent, structured output every time |

---

## How the Agent Reasons

The agent works by cycling through a four-phase loop — **Think, Action, Input, Output** — until it decides it has gathered enough information to give a final answer.

```
Think  →  What do I need to find out next?
Action →  Which capability should I use?
Input  →  What information do I pass to it?
Output →  What did I learn?
           ↓
       IsFinal = yes → deliver the answer
       IsFinal = no  → loop back to Think
```

In practice, a planning run looks like this:

| Step | What the agent does | Why |
|---|---|---|
| **1. Scope Analysis** | Defines what is in and out of scope | A clear scope prevents wasted effort and scope creep |
| **2. Risk Assessment** | Identifies the top risks and mitigation strategies | Risks caught early cost less to fix |
| **3. Action Planning** | Creates a milestone-based action plan with owners | Teams need concrete next steps, not just goals |
| **4. Final Answer** | Synthesises all findings into a single deliverable | One document the team can act on immediately |

---

## Key Capabilities

- **Scope definition** — converts a free-text goal into a scoped deliverable with clear in/out-of-scope boundaries.
- **Risk identification** — surfaces common project risks (scope creep, timeline slippage, resource gaps) and pairs each risk with a mitigation strategy.
- **Action planning** — produces a milestone-based plan with owners and review cadence.
- **Transparent reasoning** — every step the agent takes is visible in the response, so stakeholders can audit or challenge the plan.
- **Configurable depth** — the `maxIterations` setting controls how many reasoning steps the agent is allowed to take, giving you a cost/quality trade-off knob.

### Orchestrator — Full Team Simulation

The orchestrator mode dispatches the project brief to a virtual start-up delivery team using a dynamic route. For complex projects it can run the full chain, and for focused requests it can run only the required roles.

| Role | What they produce |
|---|---|
| **PO** (Product Owner) | Product vision, goals, user stories, acceptance criteria |
| **PM** (Project Manager) | Milestones, timeline, resource plan, risk register |
| **BA** (Business Analyst) | Functional requirements, use cases, gap analysis |
| **DEV** (Developer) | Tech stack choice, architecture, API design, implementation approach |
| **TEST** (Tester) | Test plan, quality gates, coverage targets, sample test cases |

Each selected role builds on what the previous selected roles produced, so the DEV's architecture is shaped by the BA's requirements, and the TEST's quality gates are aligned with the DEV's tech stack choices.

---

## How to Request a Plan

### Single-agent planning

Send a `POST` request to `/api/agent/run` with three fields:

| Field | What to put here | Example |
|---|---|---|
| `goal` | What the project must achieve, in plain language | `"Launch the self-service analytics dashboard"` |
| `context` | Any constraints or background the agent should know | `"Team of 4, 8-week deadline, budget $50k"` |
| `maxIterations` | How many reasoning steps the agent may take (1–50) | `10` (default) |

**Example request:**

```json
{
  "goal":          "Launch the self-service analytics dashboard",
  "context":       "Team of 4 engineers, 8-week deadline, budget $50k",
  "maxIterations": 10
}
```

### Full team orchestration

Send a `POST` request to `/api/orchestrator/run` with three fields:

| Field | What to put here | Example |
|---|---|---|
| `projectBrief` | High-level description of the project | `"Build a SaaS project management tool"` |
| `context` | Any constraints or background | `"Start-up, team of 5, 10-week runway"` |
| `maxIterationsPerAgent` | Reasoning steps allowed per agent (1–50) | `10` (default) |

**Example request:**

```json
{
  "projectBrief":          "Build a SaaS project management tool for remote teams",
  "context":               "Start-up phase, team of 5, 10-week runway",
  "maxIterationsPerAgent": 10
}
```

The response contains a `summary` and five `agentOutputs` — one per role — each with the full deliverable for that role.

Each orchestrated `agentOutput` now includes routing metadata used for dynamic orchestration:

| Field | Meaning |
|---|---|
| `decision` | Agent routing recommendation (`continue`, `stop`, `escalate`) |
| `confidence` | Confidence score from `0.0` to `1.0` |
| `issues` | Explicit blockers or concerns that can change route selection |
| `nextAction` | Suggested next transition for the orchestrator |

---

## What the Response Looks Like

The agent returns:

1. **`finalAnswer`** — a human-readable synthesis of the full plan.
2. **`steps`** — the complete reasoning trace, one entry per loop iteration.

Each step records:

| Field | Meaning |
|---|---|
| `thought` | Why the agent chose this action |
| `action` | Which capability was used |
| `actionOutput` | What the capability produced |
| `isFinal` | Whether this was the last step |

**Example output summary:**

> _"Agent completed planning for goal: 'Launch the self-service analytics dashboard'."_
>
> **Scope:** Deliver a working analytics dashboard accessible to business users. Core deliverables are time-boxed and measurable. Out-of-scope items are documented.
>
> **Risks:** (1) Scope creep — mitigate with change-control. (2) Timeline slippage — mitigate with weekly milestone reviews. (3) Resource gaps — assign backup owners.
>
> **Action plan:** (1) Clarify acceptance criteria. (2) Break work into weekly milestones. (3) Assign owner + due date to every task. (4) Maintain a risk register reviewed weekly.

---

## Understanding the `isFinal` Flag

The agent loops through reasoning steps until it is confident it has a complete answer. The `isFinal` flag is set to `true` on the last step and causes the loop to stop.

This design has two business benefits:

- **No wasted work** — the agent stops as soon as it has enough information.
- **Safety guardrail** — if something unexpected happens, `maxIterations` ensures the agent always terminates.

---

## Glossary

| Term | Meaning |
|---|---|
| **Goal** | The outcome the project must achieve, stated in plain language |
| **Context** | Background information that helps the agent reason (e.g., constraints, team size) |
| **AgentStep** | One iteration of the reasoning loop: Think → Action → Input → Output |
| **IsFinal** | A flag that signals the agent is done reasoning and ready to deliver the answer |
| **Scope Analysis** | The process of defining what is and is not part of the project |
| **Risk Assessment** | The process of identifying what could go wrong and how to prevent it |
| **Action Plan** | A structured list of next steps with owners and deadlines |
| **MaxIterations** | The maximum number of reasoning steps the agent may take in one run |
| **Orchestrator** | The coordinator that dispatches the project brief to each specialized agent in sequence |
| **Specialized Agent** | A virtual team member (PO, PM, BA, DEV, TEST) that produces a role-specific deliverable |
| **ProjectBrief** | A plain-language description of the project handed to the orchestrator |
| **AgentTaskResult** | The deliverable produced by a single specialized agent |
| **OrchestrationResult** | The aggregated output from all specialized agents plus a cross-role summary |
