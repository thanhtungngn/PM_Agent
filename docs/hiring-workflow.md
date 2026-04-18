# PM Agent - Hiring Workflow

> Audience: recruiters, hiring managers, project managers, business analysts, and technical interviewers.

This document describes the staged hiring process that starts with HR-only screening and then expands to PM, BA, and one technical interviewer only after the user approves the next step.

## Workflow overview

The hiring process is not a one-shot orchestration. It is a stateful workflow with approvals and interview turns.

### Step 1 - HR-only CV screening

HR is the only role involved at the beginning.

HR responsibilities:

- Read the CV.
- Extract keywords and evidence.
- Compare CV evidence against the JD.
- Calculate a screening fit score.
- Ask the user whether the CV should be forwarded when the fit score is above 70%.

If the fit score is below 70%, the process stops and HR records the outcome in the notes document.

### Step 2 - User approval to forward the CV

When the fit score is above the threshold, HR asks the user for approval.

If the user approves, the CV is forwarded to:

- `PM`
- `BA`
- one technical interviewer: `DEV` or `TEST`

If the user does not approve, the process stops.

### Step 3 - PM schedules the interview

PM prepares the interview schedule and explains the panel structure.

The workflow supports two modes:

- explicit user approval before starting the interview
- auto-approval for the schedule (current default)

### Step 4 - Interview opening

The interview starts with role introductions in sequence:

- HR introduces themselves and explains that they will keep interview notes.
- PM introduces themselves and provides a brief overview of the project.
- DEV or TEST introduces themselves as the technical interviewer.
- BA introduces themselves as the scenario and stakeholder interviewer.

Then the candidate is asked to introduce themselves.

### Step 5 - PM interview turn

PM explains the project context and asks project-oriented questions such as:

- which part of the project the candidate would contribute to first
- how the candidate thinks about delivery ownership
- how the candidate handles constraints and trade-offs

### Step 6 - DEV or TEST technical interview turn

Exactly one technical interviewer joins the panel.

- `DEV` asks software engineering and architecture questions
- `TEST` asks QA, test strategy, automation, and quality questions

### Step 7 - BA scenario interview turn

BA asks behavior and scenario questions, for example:

- changing requirements late in the cycle
- handling stakeholder misalignment
- clarifying scope under ambiguity

### Step 8 - HR note-taking throughout the interview

After each candidate answer, HR records a note in the transcript.

The note captures:

- the key point from the answer
- the signal strength of the response
- whether the answer should influence the final decision

### Step 9 - Interview scoring agent

A scoring agent continuously evaluates the interview transcript.

It updates the interview score after each candidate response and can stop the interview early when the score drops too low.

The session also ends normally when the required interview objectives have been covered.

### Step 10 - Q/A and closing

When the panel finishes the required questions:

- the team moves to a Q/A segment
- the candidate can ask questions
- the panel closes the session and says goodbye

HR then writes the full interview notes to a markdown document file.

## API flow

### 1. Start the session

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

### 2. Approve forwarding after HR screening

`POST /api/hiring/sessions/{sessionId}/approve-screening`

```json
{
  "approved": true,
  "comment": "Forward the CV to the panel."
}
```

### 3. Approve interview start when PM schedule approval is manual

`POST /api/hiring/sessions/{sessionId}/approve-interview`

```json
{
  "approved": true,
  "comment": "Start the interview now."
}
```

### 4. Submit candidate responses turn by turn

`POST /api/hiring/sessions/{sessionId}/candidate-response`

```json
{
  "message": "I have spent six years building and operating .NET APIs for SaaS products."
}
```

### 5. Query current state at any time

`GET /api/hiring/sessions/{sessionId}`

### 6. Export interview notes when available

`GET /api/hiring/sessions/{sessionId}/notes`

## Session outputs

Each session returns:

- stage
- approval state
- current speaker
- current prompt
- screening fit score
- interview score
- transcript
- notes document path

## Termination rules

The interview ends when one of the following becomes true:

- HR screening fit score is below 70%
- the user declines forwarding or declines the interview schedule
- the interview score becomes too low during the interview
- the required interview objectives have been covered

## Notes document

At the end of the workflow, HR writes a markdown file containing:

- project brief
- JD
- CV
- transcript by speaker
- screening fit score
- final interview score
- closing summary

The file is written to `docs/interviews/` when that folder is available in the running environment.

## Browser support

The browser UI at `/` can run this workflow end to end.

It supports:

- starting a hiring session
- approving HR screening and PM scheduling
- sending candidate answers turn by turn
- viewing the transcript as a timeline
- exporting the generated notes document
- restoring the active session from browser storage

The JD and CV fields also support plain-text file upload for quicker setup.

Current limitation: the upload helper is designed for text-based files only. Binary formats such as PDF or DOCX are not parsed by the browser client.