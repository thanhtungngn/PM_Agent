# PM Agent - Hiring Workflow

> Audience: recruiters, hiring managers, project managers, business analysts, and technical interviewers.

This document describes the current staged hiring workflow implemented by `IHiringWorkflowService` and exposed under `/api/hiring/sessions`.

It reflects the current runtime behavior, not the earlier keyword-only or fully hard-coded flow.

## Current model

The hiring process is a stateful workflow with approvals, interview turns, per-candidate artifacts, and continuous scoring.

The main design choices are:

- HR screening is LLM-first and semantic, not just keyword overlap.
- The first interview context comes from the project brief, JD, and CV, but each next runtime question is generated from the live markdown notes file for the session.
- The interview can branch into follow-up, clarification, and hint subflows without losing the active question context.
- The interviewer can answer candidate questions conversationally and switch language mid-session when the candidate does.
- Interview scoring is updated after every candidate answer and can stop the session early.
- Each session creates a candidate-specific folder with reusable artifacts and a live Q&A log.

## Stages

The workflow uses these stages:

| Stage | Meaning |
|---|---|
| `awaiting_screening_approval` | HR screening passed and the system is waiting for the user to approve forwarding the CV |
| `awaiting_interview_approval` | PM prepared the schedule and the system is waiting for the user to approve starting the interview |
| `interview_active` | The panel interview is in progress |
| `completed` | The interview or approval flow ended normally and notes were written |
| `rejected` | The candidate was rejected during screening or the user stopped the process early |

`created` exists only as an internal bootstrap state before the first result is returned.

## End-to-end flow

### 1. Session start and HR semantic screening

The workflow starts with:

`POST /api/hiring/sessions`

Input fields:

- `projectBrief`
- `jobDescription`
- `candidateCv`
- `context`
- `technicalInterviewRole` (`DEV` or `TEST`, default `DEV`)
- `autoApproveInterviewSchedule` (default `true`)

At start, the system:

- normalizes the technical interviewer to `DEV` or `TEST`
- calls `IHiringFitScoringAgent` to evaluate semantic fit between project brief, JD, CV, and selected technical role
- stores:
  - screening score
  - screening summary
  - strengths
  - gaps
- creates a candidate folder immediately
- writes an HR screening summary into the transcript

If either of these is true, the session is rejected immediately:

- `ShouldAdvance = false` from the fit scorer
- `ScreeningFitScore < HiringWorkflow.ScreeningPassThreshold`

In that case:

- `Stage = rejected`
- notes are written immediately
- no PM / BA / technical interview stage is started

### 2. User approval to forward the CV

If screening passes:

- `Stage = awaiting_screening_approval`
- `RequiresUserApproval = true`
- `ApprovalType = screening_forward`

The current prompt asks whether the CV should be forwarded to:

- `PM`
- `BA`
- exactly one technical interviewer: `DEV` or `TEST`

If the user rejects forwarding:

- `Stage = rejected`
- notes are written
- the workflow stops

If the user approves forwarding:

- participants become `HR`, `PM`, `BA`, and the selected technical interviewer
- PM writes a scheduling summary into the transcript

### 3. PM scheduling gate

The PM scheduling step always happens after screening approval.

Two modes are supported:

- auto approval: the default path, where the system inserts an automatic user approval and starts the interview immediately
- manual approval: the system waits for explicit user approval before starting the interview

If manual approval is enabled:

- `Stage = awaiting_interview_approval`
- `RequiresUserApproval = true`
- `ApprovalType = interview_schedule`

If the user rejects the schedule:

- `Stage = completed`
- notes are written
- the interview never starts

If the user approves, or auto approval is enabled, the workflow moves to `interview_active`.

### 4. Interview opening

When the interview begins, the workflow adds panel introductions to the transcript:

- `HR`: communication observer and note keeper
- `PM`: project context interviewer
- `DEV` or `TEST`: technical depth interviewer
- `BA`: stakeholder and requirements interviewer

These introductions are language-aware. The runtime picks an initial conversation language from the hiring materials and can update that language later from candidate messages.

After the introductions, the workflow builds the interviewer plan.

### 5. Question generation and interview queue

Questions are built by `IInterviewQuestionProvider`.

Current behavior:

- the workflow keeps an interviewer plan: PM questions first, then technical, then BA, then HR closing
- before each new interviewer turn, the service writes the latest `hiring-session-{sessionId}.md` notes file
- the provider reads that markdown file and asks the LLM for exactly one next question for the requested speaker
- the markdown notes already contain the JD, CV, transcript, and prior HR/EVAL context, so the runtime prompt does not need to resend raw JD/CV fields every turn
- if LLM generation fails or returns invalid output, the provider falls back to configured templates

The current default order is:

1. One or more PM general questions generated from the project brief, JD, and CV
2. One technical interviewer question (`DEV` or `TEST`)
3. BA scenario question
4. HR closing / Q&A question

The first PM question usually acts as the candidate introduction / relevance question. The exact number of PM general questions is configurable through `HiringWorkflow.GeneralQuestionCount`.

## Interview behaviors inside `interview_active`

### Normal answer flow

Each candidate answer submitted via:

`POST /api/hiring/sessions/{sessionId}/candidate-response`

goes through this sequence:

1. Candidate answer is appended to the transcript.
2. Candidate answer is appended to `interview-qa.md`.
3. HR adds a short interview note to the transcript.
4. The scoring agent reevaluates the transcript.
5. The workflow either:
   - stops early,
   - asks a follow-up,
   - or advances to the next queued question.

### Follow-up questions

Yes, this should be documented as part of the current process because it is now a first-class behavior in the workflow.

Each `InterviewQuestion` may include one optional `FollowUpText`.

Current behavior:

- after a primary answer is accepted and scored, the workflow checks the active question
- if that question has `FollowUpText` and it has not been used yet, the same interviewer immediately asks the follow-up
- the workflow stays on the same logical question until that follow-up is consumed
- only one follow-up is allowed per question

In the current implementation, these follow-up questions are expected to come from the LLM-generated interview pack first, not from hard-coded runtime branching.

API signals exposed to the client:

- `FollowUpAvailable = true` when the active question has an unused follow-up
- `PendingFollowUp` contains the follow-up text for the active question

Important detail:

- `PendingFollowUp` is the follow-up text attached to the active question
- `FollowUpAvailable` tells you whether it is still unused

### Clarification flow

If the candidate message looks like a clarification question or a conversational interviewer request, for example:

- it ends with `?`
- it contains phrases like `could you clarify`, `what do you mean`, `can you explain`

then the workflow does not treat that message as the candidate's final answer.

Instead it:

- logs the candidate clarification into the transcript and Q&A log
- writes the latest markdown notes first
- generates an interviewer reply from the live notes markdown so the answer stays grounded in the real session context
- keeps the same interviewer active
- keeps the same main question logically active
- mirrors the candidate's language and can acknowledge a language switch request immediately

The result status becomes: interviewer answered the clarification, please continue with your answer.

### Hint flow

The candidate can request a hint in either of two ways:

- submit `candidate-response` with `IsHintRequest = true`
- call `POST /api/hiring/sessions/{sessionId}/hint`
- ask naturally in the current conversation language, for example `give me a hint` or `bạn có thể cho tôi gợi ý được không`

Current behavior:

- if the active question has `HintKeywords`, the system returns up to `HiringWorkflow.HintKeywordCount` keywords
- otherwise it derives fallback hints from JD keywords
- the hint is written to the transcript and marked as `[HINT]` in `interview-qa.md`
- the logical active question does not advance

Important implementation detail:

- the workflow keeps the same active question
- but `CurrentPrompt` is temporarily updated to the hint text that was just given

So the session remains on the same question, but the immediate prompt shown to the UI is the hint text rather than the original question text.

### Scoring and early stop

After every accepted candidate answer, the workflow calls `IInterviewScoringAgent`.

Current scoring model:

- primary path: LLM-based evaluation
- fallback path: conservative non-evaluating fallback if the LLM output is unavailable or invalid

The evaluator returns:

- overall `Score`
- `ShouldStop`
- short rationale
- optional dimension breakdowns such as:
  - `communication`
  - `problem_solving`
  - `technical_judgment`
  - `ownership`
  - `collaboration`

The evaluator also calibrates expectations by resolved seniority. Junior candidates are measured more on fundamentals and coachability, mid-level candidates on independent execution and decision quality, and senior candidates on ambiguity handling, leadership, and wider team impact.

If both are true:

- `ShouldStop = true`
- candidate response count has reached `MinimumResponsesBeforeStop`

then the interview is closed early.

Important detail:

- the fallback no longer scores candidate transcript text using keyword overlap or other text heuristics
- if semantic evaluation is unavailable, the system returns a conservative fallback result instead of attempting transcript-based scoring

### Normal completion

If there are no more queued questions after the current answer/follow-up cycle, the workflow completes normally.

Closing behavior:

- PM adds a closing thank-you message
- HR adds a closing message and next-steps note
- notes file is written
- `Stage = completed`

## Candidate folder and artifacts

At session start, the workflow creates:

`candidate-{sessionId}/`

under the notes root.

By default the notes root is:

- `docs/interviews/` if a `docs` folder is found while walking up from the current working directory
- otherwise `interview-notes/` under the current working directory

Artifacts written into the candidate folder:

| File | Purpose |
|---|---|
| `jd-keywords.md` | Extracted JD keywords plus original JD |
| `cv-keywords.md` | Extracted CV keywords, matched JD keywords, plus original CV |
| `interview-qa.md` | Live Q&A log with questions, candidate answers, clarifications, and hints |
| `hiring-session-{sessionId}.md` | Markdown notes file that is refreshed during the session and also serves as the live context source for generating the next interviewer question |

## API flow

### 1. Start session

`POST /api/hiring/sessions`

```json
{
  "projectBrief": "Hire a backend engineer for the platform team",
  "jobDescription": "Need C#, ASP.NET Core, PostgreSQL, Docker, and API design experience.",
  "candidateCv": "Built ASP.NET Core APIs with PostgreSQL, Docker, Azure deployment pipelines, and production support.",
  "context": "Remote-first team, two interviewers available this week.",
  "technicalInterviewRole": "DEV",
  "autoApproveInterviewSchedule": true
}
```

### 2. Approve or reject screening forward

`POST /api/hiring/sessions/{sessionId}/approve-screening`

```json
{
  "approved": true,
  "comment": "Forward the CV to the panel."
}
```

### 3. Approve or reject interview start when manual scheduling is enabled

`POST /api/hiring/sessions/{sessionId}/approve-interview`

```json
{
  "approved": true,
  "comment": "Start the interview now."
}
```

### 4. Submit a candidate answer

`POST /api/hiring/sessions/{sessionId}/candidate-response`

```json
{
  "message": "I have spent six years building and operating .NET APIs for SaaS products."
}
```

### 5. Submit a hint request through candidate-response

`POST /api/hiring/sessions/{sessionId}/candidate-response`

```json
{
  "message": "I need a hint",
  "isHintRequest": true
}
```

### 6. Request a hint directly

`POST /api/hiring/sessions/{sessionId}/hint`

### 7. Query current session state

`GET /api/hiring/sessions/{sessionId}`

### 8. Export final notes

`GET /api/hiring/sessions/{sessionId}/notes`

## Session result fields that matter for review

Each `HiringSessionResult` returns:

- `sessionId`
- `stage`
- `requiresUserApproval`
- `approvalType`
- `screeningFitScore`
- `interviewScore`
- `currentSpeaker`
- `currentPrompt`
- `statusSummary`
- `technicalInterviewRole`
- `notesDocumentPath`
- `participants`
- `transcript`
- `followUpAvailable`
- `pendingFollowUp`
- `candidateFolder`

These are the most important fields for building UI state and for reviewing whether the workflow is behaving correctly.

## Current termination rules

The workflow ends when one of these conditions is met:

- semantic screening fails the threshold
- the user rejects CV forwarding
- the user rejects interview start
- the scoring agent decides the interview should stop early after the minimum response count is reached
- the queued interview objectives are exhausted and the workflow closes normally

## Current review notes

These are the main differences from the earlier hiring flow:

- screening is semantic and LLM-first, not keyword-only
- the full interview pack, including follow-up questions, is generated from JD + CV + project brief
- follow-up questions are explicit workflow behavior and should be documented
- hints and clarifications are now part of the active interview loop
- scoring no longer depends on answer length bands or transcript keyword heuristics; it is primarily LLM-based with dimension breakdowns and a conservative fallback