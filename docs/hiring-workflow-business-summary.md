# PM Agent - Hiring Process Summary

> Audience: recruiters, hiring managers, PMs, and business stakeholders who want a fast understanding of how the current hiring flow works without reading the full architecture document.

This is the short version of the Hiring Process. For the full architecture and technical trade-offs, see [hiring-workflow.md](hiring-workflow.md).

---

## What This Process Does

The current Hiring Process helps the team move from CV intake to a guided, scored interview session with clear approval checkpoints and review artifacts.

In simple terms, the system:

1. Screens the candidate against the project and JD.
2. Waits for approval before forwarding the CV.
3. Starts a structured panel interview only after approval.
4. Locks the interview language to English or Vietnamese at the start.
5. Asks a focused set of interview questions based on the current JD priorities and the candidate's demonstrated skills.
6. Scores each real answer and can stop early if the candidate is clearly below the bar.

---

## What Stakeholders See

From a recruiter or hiring manager perspective, the flow looks like this:

| Step | What Happens | Why It Matters |
|---|---|---|
| HR screening | The system reviews project brief, JD, and CV semantically | Reduces weak-fit candidates early |
| Approval to forward CV | A human decides whether the candidate should move forward | Keeps control with the hiring team |
| Interview start | PM and technical interviewer enter the flow | Keeps the process structured |
| Language selection | Candidate chooses English or Vietnamese once | Avoids mixed-language interviews |
| Technical interview | Questions focus on current JD priorities and proven candidate overlap | Makes the interview more relevant |
| Live scoring | Each accepted answer is evaluated | Gives the team a running signal, not just an end-of-session guess |
| Notes and artifacts | The session produces review files and transcript context | Supports post-interview review and auditability |

---

## What Makes the Current Model Better Than a Basic Interview Bot

### 1. It is not keyword-only anymore

The process does not rely only on matching isolated words from the JD and CV. It uses semantic screening first, then builds interview focus around meaningful skill areas.

### 2. It is still controlled by the team

The system does not auto-run a full panel interview without approval. Human checkpoints remain in the flow.

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

---

## Current Strengths

### Strong Fit Filtering Early

Candidates who are clearly off-target can be filtered before the team spends time on a full interview.

### Better Interview Relevance

Question focus is driven by the current JD and project context, not by a generic library of interview topics.

### Better Language Consistency

The interview now locks to one language at the start, which improves clarity for both candidate and panel.

### Better Reviewability

Each session writes notes, Q&A logs, and candidate-specific artifacts that can be reviewed later.

### Better Scoring Behavior

The score is updated per answer instead of being based only on a vague overall transcript impression.

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
| Developers implementing the flow | [technical.md](technical.md) |

---

## Bottom Line

The current Hiring Process is a controlled, AI-assisted workflow rather than an open-ended interview bot.

That is the right trade-off for the current stage of the solution:

- strong enough to improve relevance and efficiency
- controlled enough to stay reviewable and trustworthy
- simple enough to operate without turning the interview into an opaque black box