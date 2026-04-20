# PM Agent - Hiring Process Summary

> Audience: recruiters, hiring managers, PMs, and business stakeholders who want a fast understanding of how the current hiring flow works without reading the full architecture document.

This is the short version of the Hiring Process. For the full architecture and technical trade-offs, see [hiring-workflow.md](hiring-workflow.md).

---

## What This Process Does

The current Hiring Process helps the team move from CV intake to a guided, scored interview session with clear approval checkpoints and review artifacts.

There are two usage modes:

**Staged interview workflow** — the full turn-by-turn experience:

1. Screens the candidate against the project and JD.
2. Waits for approval before forwarding the CV.
3. Starts a structured panel interview only after approval.
4. Locks the interview language to English or Vietnamese at the start.
5. Asks a focused set of interview questions based on the current JD priorities and the candidate's demonstrated skills.
6. Scores each real answer and can stop early if the candidate is clearly below the bar.

**One-shot hiring orchestration** — rapid single-pass assessment via `HiringOrchestrationAgent`:

1. Submits project brief, JD, and CV to the orchestrator with `workflow=hiring`.
2. The agent reasons through CV analysis, JD fit, and interview planning in one LLM-driven pass.
3. Returns a structured markdown report: CV analysis, fit rating per requirement, screening decision, interview plan, and final recommendation.
4. No approval steps or multi-turn interview required.

Use the staged workflow when a real interactive interview is needed. Use the one-shot path for rapid pre-screening, bulk evaluation, or when a written assessment is sufficient.

---

## What Stakeholders See

From a recruiter or hiring manager perspective, the staged workflow looks like this:

| Step | What Happens | Why It Matters |
|---|---|---|
| HR screening | The system reviews project brief, JD, and CV semantically | Reduces weak-fit candidates early |
| Approval to forward CV | A human decides whether the candidate should move forward | Keeps control with the hiring team |
| Interview start | PM and technical interviewer enter the flow | Keeps the process structured |
| Language selection | Candidate chooses English or Vietnamese once | Avoids mixed-language interviews |
| Technical interview | Questions focus on current JD priorities and proven candidate overlap | Makes the interview more relevant |
| Live scoring | Each accepted answer is evaluated | Gives the team a running signal, not just an end-of-session guess |
| Notes and artifacts | The session produces review files and transcript context | Supports post-interview review and auditability |

The one-shot orchestration path (`HiringOrchestrationAgent`) produces a structured written report instead of a live interview:

| Section | What It Contains |
|---|---|
| CV Analysis | Candidate skills, experience, notable projects, strengths and gaps |
| JD Fit Assessment | Per-requirement rating (Strong Match / Partial Match / Gap) |
| Screening Decision | Proceed / Hold / Reject with confidence % and rationale |
| Interview Plan | Stages, panel composition, estimated duration, sample questions |
| Final Recommendation | 2–3 sentence synthesis with concrete next steps |

---

## What Makes the Current Model Better Than a Basic Interview Bot

### 1. It is not keyword-only anymore

The process does not rely only on matching isolated words from the JD and CV. It uses semantic screening first, then builds interview focus around meaningful skill areas.

### 2. It is still controlled by the team

The system does not auto-run a full panel interview without approval. Human checkpoints remain in the staged flow. The one-shot path produces a written report that can be reviewed before committing to an interview.

### 3. It behaves more like a real interview

The candidate can:

- ask for clarification
- request a hint
- ask limited side questions

The interviewer can respond naturally without scoring those turns as if they were real answers.

### 4. It uses a more relevant technical focus

The interview prioritizes high-value categories such as:

- programming languages
- frameworks
- system design
- databases
- agile / scrum delivery methodology

This makes the question set much closer to the real job than a generic interview script.

### 5. It supports both interactive and rapid-assessment modes

- Use the staged interview for real live interaction with a candidate.
- Use `HiringOrchestrationAgent` for pre-screening, batch assessment, or when a written report is enough to make a forwarding decision.

---

## Current Strengths

### Strong Fit Filtering Early

Candidates who are clearly off-target can be filtered before the team spends time on a full interview. `HiringOrchestrationAgent` provides a fast pre-screening pass; the staged workflow adds approval-gated depth after that.

### Better Interview Relevance

Question focus is driven by the current JD and project context, not by a generic library of interview topics.

### Better Language Consistency

The interview now locks to one language at the start, which improves clarity for both candidate and panel.

### Better Reviewability

Each session writes notes, Q&A logs, and candidate-specific artifacts that can be reviewed later. The one-shot path produces a structured markdown report that serves as a standalone review document.

### Better Scoring Behavior

The score is updated per answer instead of being based only on a vague overall transcript impression.

### Two Complementary Hiring Paths

The system now supports both rapid one-shot assessment (`HiringOrchestrationAgent`) and full interactive interview sessions, so teams can choose the right level of depth for each candidate or stage.

---

## Current Weaknesses

### The system still depends on a maintained skill catalog

If a stack or methodology is uncommon and not yet represented well, question focus may still miss it.

### The main question plan is generated once

This improves control, but it also means the system is less adaptive than a fully dynamic replanning model.

### Session state is still in memory

This is acceptable for local or internal usage, but it is not ideal for durable production-scale operations.

### The current panel model is intentionally simplified

The runtime is focused on `HR + PM + one technical interviewer`, so it is not yet a full multi-role hiring committee design.

---

## Recommended Reading by Audience

| Audience | Best Document |
|---|---|
| Recruiter / Hiring manager | [hiring-workflow-business-summary.md](hiring-workflow-business-summary.md) |
| Solution Architect / Engineering lead | [hiring-workflow.md](hiring-workflow.md) |
| Developers implementing the staged flow | [technical.md](technical.md) — `IHiringWorkflowService` section |
| Developers using the one-shot path | [technical.md](technical.md) — `HiringOrchestrationAgent` in Specialized Agents section |

---

## Bottom Line

The current Hiring Process offers two complementary modes:

- The **staged interview workflow** is a controlled, AI-assisted process with step-by-step approvals and a real-time interactive interview. Use it when human oversight and conversational realism matter.
- The **one-shot `HiringOrchestrationAgent` path** provides a rapid LLM-driven written assessment with no interactive session required. Use it for pre-screening, batch evaluation, or when a structured report is sufficient.

Both modes are:

- strong enough to improve relevance and efficiency over purely manual review
- controlled enough to stay reviewable and trustworthy
- simple enough to operate without turning the hiring process into an opaque black box