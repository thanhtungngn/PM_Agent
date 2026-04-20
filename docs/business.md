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
Think  →  What do I need to find out next? (LLM-driven)
Action →  Which capability should I use?
Input  →  What information do I pass to it?
Output →  What did I learn? (stored in agent memory)
           ↓
       IsFinal = yes → deliver the answer
       IsFinal = no  → loop back to Think
```

The Think step now calls the LLM with the goal, accumulated memory context, and the list of available capabilities. The LLM responds with a structured JSON decision — `{"thought": "...", "action": "scope_analysis", "actionInput": "..."}` — rather than following a hard-coded sequence. If the LLM is unavailable, a deterministic fallback sequence runs instead.

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
- **LLM-driven ReAct loop** — the agent's Think step now calls the LLM to decide which tool to use next, given the goal and accumulated context. The LLM produces structured JSON (`thought`, `action`, `actionInput`) rather than following a hard-coded tool sequence. If the LLM is unavailable, the system falls back automatically to a deterministic rule-based sequence.
- **LLM-backed planning** — `LlmAgentPlanner` calls the LLM with project name, goal, team, and constraints to produce context-aware plans with concrete next actions and risks, instead of returning a static template.
- **Agent memory** — each agent run maintains an `IAgentMemory` instance that records all tool outputs and context entries in insertion order. The accumulated context is passed into each subsequent LLM Think step so reasoning is grounded in prior work rather than reconstructed from scratch.
- **LLM-driven orchestrator routing** — after each specialized agent completes, the orchestrator asks the LLM which role should run next (`continue`, `stop`, or `escalate`). This makes the multi-agent routing dynamic rather than fully pre-planned. Rule-based fallback applies automatically when the LLM is unavailable.
- **One-shot hiring assessment** — `HiringOrchestrationAgent` (role `HIRING_ORC`) performs a complete end-to-end hiring evaluation in a single LLM-driven pass: CV analysis, JD fit per requirement, screening decision with confidence, interview plan, and final recommendation. Useful for rapid pre-screening or bulk CV evaluation without running a live multi-turn interview.
- **Staged hiring workflow** — supports HR-only screening first, explicit user approvals, role-based interview turns, interview scoring, per-candidate file folder, live Q&A logging, structured follow-up questions, clarification replies, candidate hints, and persisted HR notes.
- **LLM-first interview system** — the hiring flow now asks the LLM once to generate a technical interview question bank from the project brief, JD, and CV, then uses runtime prompts only for clarification, feedback, and follow-up control. For each hiring request, it first infers the current JD's stack priorities and gives extra weight to programming languages, frameworks, system design, databases, and agile/scrum methodology, then biases technical questions toward the candidate's demonstrated overlap with those exact priorities, with a smaller number of transferability questions for important but less-proven requirements. Candidate-facing prompts and evaluation notes also mirror the dominant language of the interview context when possible.
- **Locked interview language** — before the main questions begin, the interviewer asks the candidate to choose English or Vietnamese. The rest of the interview then follows that one language consistently, including generated questions, fallback templates, clarification replies, and feedback turns.
- **Interviewer feedback loop** — after each accepted candidate answer, the active interviewer adds a short feedback turn so the session feels like a real conversation instead of a one-way questionnaire. The tone is intentionally conversational and interviewer-like, rather than a visible evaluator note.
- **Conversational interview guardrails** — when the candidate is not really answering yet and is instead asking for clarification, process details, or other side information, the interviewer replies naturally without scoring that turn as an answer. If this happens too many times on the same question, the interviewer gently redirects the conversation back to the active interview question.
- **Quality harness** — a built-in scenario runner that validates all agent roles without requiring a live LLM. Produces JSON and Markdown reports in `harness-reports/` and is designed for use in CI pipelines. Trigger a run at any time via `POST /api/harness/run` or from the GitHub Actions CI workflow.

### Orchestrator — Full Team Simulation

The orchestrator mode dispatches the project brief to a virtual start-up delivery team using a dynamic route. For complex projects it can run the full chain, and for focused requests it can run only the required roles.

| Role | What they produce |
|---|---|
| **PO** (Product Owner) | Product vision, goals, user stories, acceptance criteria |
| **PM** (Project Manager) | Milestones, timeline, resource plan, risk register |
| **HR** (Human Resources) | Hiring plan, staffing priorities, interview process, onboarding plan |
| **BA** (Business Analyst) | Functional requirements, use cases, gap analysis |
| **DEV** (Developer) | Tech stack choice, architecture, API design, implementation approach |
| **TEST** (Tester) | Test plan, quality gates, coverage targets, sample test cases |
| **HIRING_ORC** (Hiring Orchestration Agent) | End-to-end candidate assessment: CV analysis, JD fit per requirement, screening decision, interview plan, and final recommendation — all in one LLM-driven pass |

Each selected role builds on what the previous selected roles produced, so the DEV's architecture is shaped by the BA's requirements, and the TEST's quality gates are aligned with the DEV's tech stack choices. After each role completes, the orchestrator asks the LLM which role should run next, making the dispatch dynamic rather than fully pre-planned.

---

## How to Request a Plan

### Browser chat UI

Open `/` in the running API host to use the built-in chat console.

The chat UI supports:

- Delivery workflow prompts.
- Hiring workflow prompts.
- Optional hiring inputs for JD, CV, and technical interview packs.
- Interactive hiring sessions with approval actions and role-by-role candidate responses.
- Markdown rendering for agent outputs.
- Hiring transcript timeline and active session restore in the browser.
- Interview notes export after HR closes the session.
- Text-file upload to prefill JD and CV fields quickly.
- Local conversation history stored in the browser.
- Preset scenarios for delivery and hiring.
- Sample request payload preview with copy support.
- Quick links to Swagger and health checks.

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

Send a `POST` request to `/api/orchestrator/run` with the following fields:

| Field | What to put here | Example |
|---|---|---|
| `projectBrief` | High-level description of the project | `"Build a SaaS project management tool"` |
| `context` | Any constraints or background | `"Start-up, team of 5, 10-week runway"` |
| `maxIterationsPerAgent` | Reasoning steps allowed per agent (1–50) | `10` (default) |
| `workflow` | Orchestration mode: `delivery` or `hiring` | `"hiring"` |
| `jobDescription` | Required when `workflow = hiring`; the JD used for screening | `"Senior backend engineer with .NET and PostgreSQL"` |
| `candidateCv` | Required when `workflow = hiring`; extracted CV text | `"5 years in ASP.NET Core, SQL, Docker..."` |
| `targetSeniority` | Optional in hiring mode; expected candidate level for question and score calibration | `"SENIOR"` |
| `technicalInterviewRoles` | Optional in hiring mode; which technical interview packs to generate | `["DEV", "TEST"]` |

**Example request:**

```json
{
  "projectBrief":          "Build a SaaS project management tool for remote teams",
  "context":               "Start-up phase, team of 5, 10-week runway",
  "maxIterationsPerAgent": 10,
  "workflow":              "delivery"
}
```

### Hiring workflow orchestration

Use hiring mode when you want the orchestrator to read a CV, evaluate its semantic fit against a JD, and prepare an interview process.

In hiring mode, the orchestrator starts with `PM -> HR -> BA` and then appends `DEV` and/or `TEST` when technical interview packs are requested.

**Example request:**

```json
{
  "projectBrief": "Hire a backend engineer and QA engineer for the next release",
  "context": "Remote-first team, budget approved, two interview slots per week",
  "maxIterationsPerAgent": 10,
  "workflow": "hiring",
  "jobDescription": "Need strong C#, ASP.NET Core, PostgreSQL, API design, test automation, and CI/CD experience.",
  "candidateCv": "Candidate has 5 years in .NET, REST APIs, PostgreSQL tuning, Playwright, xUnit, and Azure DevOps.",
  "targetSeniority": "SENIOR",
  "technicalInterviewRoles": ["DEV", "TEST"]
}
```

For a full process guide, see [docs/hiring-workflow.md](hiring-workflow.md).
For a shorter stakeholder-facing version, see [docs/hiring-workflow-business-summary.md](hiring-workflow-business-summary.md).

### Interactive hiring interview

Use the dedicated hiring session API when you want the process to follow an approval-based interview flow instead of one-shot orchestration.

The staged process is:

1. HR screens the CV first using an LLM-based semantic fit assessment.
2. If fit is above 40%, HR asks the user whether the CV may be forwarded to PM and one technical interviewer.
3. PM prepares the interview schedule and can wait for approval, or proceed immediately when auto-approval is enabled.
4. The interview starts with role introductions, then the candidate introduces themselves.
5. The system resolves a seniority target (`JUNIOR`, `MID`, or `SENIOR`) from the request or the hiring materials so expectations stay consistent across the interview.
7. PM opens with one question that anchors the interview in the candidate's real experience with the project stack.
8. DEV or TEST then works through a one-shot technical question bank, with most questions aimed at the strongest overlap between the project's stack requirements and the candidate's proven experience.
9. Follow-up questions are asked only when the candidate clearly understood the original question and answered it well enough to justify going deeper.
10. If the candidate is stuck they may request a hint — the interviewer provides 2–3 short prompts without revealing the full answer.
11. If the candidate asks a clarification question mid-interview, the interviewer replies and the same question remains active.
12. HR records notes throughout the interview.
13. After each accepted answer, the active interviewer gives short feedback before the next question or follow-up, phrased like a real interviewer steering the conversation.
14. A scoring agent updates the interview score using semantic LLM evaluation plus a configurable dimension rubric, and can end the session early when the score is too low. Each answer is scored individually first, then the session score is calculated from the running average, so weak answers are not hidden by earlier strong ones. The score is based on demonstrated reasoning and role fit rather than keyword overlap, is calibrated differently for junior, mid, and senior expectations, and limits situational or hypothetical answers to at most 20% of total evaluation weight. If semantic evaluation is unavailable, the system falls back conservatively to a latest-answer heuristic.
15. The panel closes with Q/A and HR writes the interview notes to a document file.
16. A per-candidate folder (`candidate-{sessionId}/`) is created at session start with extracted JD keywords, CV keywords, and a live Q&A log that is updated in real-time throughout the interview.

This staged flow is exposed under `/api/hiring/sessions`.

The browser UI can drive this staged flow directly: HR approval, PM schedule approval, candidate turn submission, timeline review, and notes export all happen from the same page.

JD and CV inputs can also be loaded from uploaded plain-text files so the user does not need to paste long content manually.

If you prefer raw API exploration, Swagger is available at `/swagger`.

The response contains a `summary` and a route-dependent collection of `agentOutputs` — one per executed role — each with the full deliverable for that role.

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
| **ReAct Pattern** | Reasoning + Acting — the agent loop where the LLM decides the next action, a tool executes it, and the LLM reasons over the output before deciding what to do next |
| **IAgentMemory** | An append-only log that accumulates all tool outputs and context entries for a single agent run, so each Think step builds on prior work rather than starting from scratch |
| **Scope Analysis** | The process of defining what is and is not part of the project |
| **Risk Assessment** | The process of identifying what could go wrong and how to prevent it |
| **Action Plan** | A structured list of next steps with owners and deadlines |
| **MaxIterations** | The maximum number of reasoning steps the agent may take in one run |
| **Orchestrator** | The coordinator that dispatches the project brief to each specialized agent. After each agent completes, the orchestrator asks the LLM which role should run next |
| **Specialized Agent** | A virtual team member (PO, PM, HR, BA, DEV, TEST, HIRING_ORC) that produces a role-specific deliverable |
| **ProjectBrief** | A plain-language description of the project handed to the orchestrator |
| **AgentTaskResult** | The deliverable produced by a single specialized agent |
| **OrchestrationResult** | The aggregated output from all specialized agents plus a cross-role summary |
| **Workflow** | The orchestrator mode. `delivery` is for project planning; `hiring` is for CV screening, JD fit, and interview preparation |
| **JobDescription** | The target job requirements used in hiring mode |
| **CandidateCv** | Extracted CV text used for semantic fit analysis and interview preparation in hiring mode |
| **TargetSeniority** | Optional hiring input that sets the expected level directly (`AUTO`, `JUNIOR`, `MID`, `SENIOR`) |
| **TechnicalInterviewRoles** | Optional list of technical interview packs to generate in hiring mode (`DEV`, `TEST`) |
| **HiringOrchestrationAgent** | The specialized agent (`HIRING_ORC`) that performs a complete one-shot hiring assessment using LLM reasoning and `IAgentMemory`: CV analysis, JD fit, screening decision, interview plan, and final recommendation |
| **Hiring Session** | A stateful interview workflow with approvals, transcript turns, score updates, and HR notes |
| **SeniorityLevel** | The resolved hiring level used to calibrate screening, questions, and evaluation throughout the session |
| **Screening Fit Score** | The HR gate score produced by semantic LLM assessment to decide whether the CV should move beyond initial screening |
| **Interview Score** | The running score maintained during the interview to decide whether the process should continue |
| **Follow-up Question** | A secondary question asked after the candidate answers a primary interview question, before advancing to the next panel member |
| **Clarification Reply** | The interviewer's response when the candidate asks a question mid-interview; keeps the current question active |
| **Hint** | 2–3 keyword prompts provided to a candidate who is stuck on a question, without revealing the full answer |
| **Candidate Folder** | A per-session directory (`candidate-{sessionId}/`) created at session start that holds JD keywords, CV keywords, and the live Q&A log |
| **Question Pack** | The staged interview set generated primarily by the LLM for PM, technical, BA, and HR interview turns, with each next question derived from the live session notes during runtime |
| **Scoring Rubric** | The configurable interview scoring model that defines thresholds and broad evaluation dimensions for semantic LLM evaluation rather than direct keyword matching |
| **LlmAgentPlanner** | The LLM-backed implementation of `IAgentPlanner` that generates context-aware project plans with concrete next actions and risks, replacing the previous static template planner |
| **Harness** | A built-in scenario runner that validates agent outputs deterministically without a live LLM |
| **HarnessScenario** | A named test case that defines the input brief, expected output sections, decision, and optionally fault-injection flags |
| **HarnessReport** | The aggregated result of a harness run, including pass rate, per-scenario results, and timing; written to `harness-reports/` as JSON and Markdown |
| **PassRate** | The percentage of harness scenarios that passed; the CI threshold is ≥ 95% |
