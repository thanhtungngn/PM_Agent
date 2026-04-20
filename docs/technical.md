# PM Agent — Technical Documentation

> **Audience:** Developers and architects.
> For business context, see [business.md](business.md).

---

## Solution Structure

```
PMAgent.slnx
├── src/
│   ├── PMAgent.Api              → HTTP controllers, request validation,
│   │                              static browser chat UI and hiring session console in wwwroot/
│   ├── PMAgent.Application      → Interfaces + data models (no implementation)
│   │   ├── Abstractions/
│   │   │   ├── Harness/         → IHarnessScenarioProvider, IHarnessRunner,
│   │   │   │                      IHarnessAssertionEngine, IHarnessReportSink
│   │   │   └── ...              → IAgentTool, IAgentExecutor, IOrchestratorAgent,
│   │   │                          IAgentMemory, IAgentPlanner, etc.
│   │   └── Models/
│   │       ├── Harness/         → HarnessScenario, HarnessAssertion,
│   │       │                      HarnessScenarioResult, HarnessReport
│   │       └── ...              → AgentStep, AgentRunRequest, InterviewQuestion, etc.
│   ├── PMAgent.Domain           → Domain entities (extensible placeholder)
│   └── PMAgent.Infrastructure   → AgentExecutor (ReAct + IAgentMemory),
│                                  InMemoryAgentMemory, OrchestratorAgent (LLM routing),
│                                  LlmAgentPlanner, Tools/ (LLM-backed),
│                                  Agents/ (including HiringOrchestrationAgent),
│                                  Harness/, DI registration
└── tests/
    └── PMAgent.Tests            → xUnit unit tests
```

### Layer responsibilities

| Layer | Responsibility |
|---|---|
| **Api** | Expose HTTP endpoints, validate request surface, serve the browser chat UI, delegate to application/infrastructure |
| **Application** | Define `IAgentExecutor`, `IAgentTool`, `IAgentPlanner`, `ISpecializedAgent`, `IOrchestratorAgent` contracts and all request/response models — zero implementation |
| **Infrastructure** | Implement `AgentExecutor`, all tools, legacy planner, and DI wiring |
| **Domain** | Core business entities — currently a placeholder; grow into it as features are added |

---

## Agent Loop — How It Works

The executor follows a **ReAct** (Reasoning + Acting) pattern. Each iteration of the loop:

```
┌─────────────────────────────────────────────────────────┐
│  Iteration N                                            │
│                                                         │
│  1. THINK      LLM decides next action based on goal +  │
│                IAgentMemory context + completed steps   │
│                Falls back to rule-based if LLM fails    │
│                                                         │
│  2. ACTION     Select a tool by name                    │
│                                                         │
│  3. INPUT      Pass a string input to the tool          │
│                                                         │
│  4. OUTPUT     Receive the LLM-generated tool result    │
│                                                         │
│  5. IsFinal?   true  → append step, break loop         │
│                false → record output in IAgentMemory    │
└─────────────────────────────────────────────────────────┘
```

**Iteration trace (default tool sequence):**

```
Iteration 0: Think (LLM) → scope_analysis    (IsFinal: false)
Iteration 1: Think (LLM) → risk_assessment   (IsFinal: false)
Iteration 2: Think (LLM) → action_planner    (IsFinal: false)
Iteration 3: Think (LLM) → finalize          (IsFinal: true)  ← loop exits
```

### LLM-driven Think step

`AgentExecutor` now accepts an optional `ILlmClient`. When provided, the Think step sends the goal, step history, and accumulated `IAgentMemory` context to the LLM using a ReAct-style system prompt. The LLM responds with structured JSON:

```json
// To invoke a tool
{"thought": "My reasoning", "action": "scope_analysis", "actionInput": "..."}

// To finalize
{"thought": "My reasoning", "isFinal": true}
```

If the LLM is unavailable or returns an unparseable response, the executor automatically falls back to the built-in rule-based sequence (`scope_analysis → risk_assessment → action_planner → finalize`).

### IsFinal flag

`IsFinal = true` signals the agent has gathered sufficient information and is ready to emit the final answer. `MaxIterations` is the safety guard that terminates the loop if `IsFinal` is never reached.

---

## Data Models

### `AgentStep`

```csharp
// src/PMAgent.Application/Models/AgentStep.cs
record AgentStep(
    string Thought,       // The agent's reasoning before acting
    string Action,        // Tool name that was selected
    string ActionInput,   // String passed into the tool
    string ActionOutput,  // String returned by the tool
    bool   IsFinal        // true = final step; loop exits after this
);
```

### `AgentRunRequest`

```csharp
// src/PMAgent.Application/Models/AgentRunRequest.cs
record AgentRunRequest(
    string Goal,               // What the agent must accomplish
    string Context,            // Initial context to seed reasoning
    int    MaxIterations = 10  // Loop safety cap; valid range: 1–50
);
```

### `AgentRunResult`

```csharp
// src/PMAgent.Application/Models/AgentRunResult.cs
record AgentRunResult(
    string                         FinalAnswer,  // Synthesised answer from the finalize step
    IReadOnlyCollection<AgentStep> Steps         // Full trace of every iteration
);
```

### `AgentTask`

```csharp
// src/PMAgent.Application/Models/AgentTask.cs
record AgentTask(
  string Role,     // Target agent role, e.g. "PO", "PM", "HR", "BA", "DEV", "TEST"
    string Goal,     // The project brief forwarded to the agent
    string Context   // Accumulated context from all predecessor agents
);
```

### `AgentTaskResult`

```csharp
// src/PMAgent.Application/Models/AgentTaskResult.cs
record AgentTaskResult(
    string Role,    // Role token of the agent that produced this result
    string Output,  // Markdown-formatted deliverable from that agent
  bool   Success, // Whether the agent completed its task successfully
  string Decision = "continue", // Routing recommendation: continue|stop|escalate
  double Confidence = 0.8, // Confidence score in [0.0..1.0]
  IReadOnlyCollection<string>? Issues = null, // Blocking issues for routing decisions
  string NextAction = "continue" // Suggested next transition for the orchestrator
);
```

### `OrchestrationRequest`

```csharp
// src/PMAgent.Application/Models/OrchestrationRequest.cs
record OrchestrationRequest(
    string ProjectBrief,              // High-level description of the project
    string Context = "",              // Optional background context
  int    MaxIterationsPerAgent = 10, // Loop safety cap per agent; valid range: 1–50
  string Workflow = "delivery",     // delivery | hiring
  string JobDescription = "",       // Required in hiring mode
  string CandidateCv = "",          // Required in hiring mode
  IReadOnlyCollection<string>? TechnicalInterviewRoles = null // Optional: DEV and/or TEST
);
```

### `OrchestrationResult`

```csharp
// src/PMAgent.Application/Models/OrchestrationResult.cs
record OrchestrationResult(
    string                               Summary,      // Stitched summary across all agents
    IReadOnlyCollection<AgentTaskResult> AgentOutputs  // One result per specialized agent
);
```

### `HiringSessionStartRequest`

```csharp
// src/PMAgent.Application/Models/HiringSessionStartRequest.cs
record HiringSessionStartRequest(
  string ProjectBrief,
  string JobDescription,
  string CandidateCv,
  string Context = "",
  string TargetSeniority = "AUTO",
  string TechnicalInterviewRole = "DEV",
  bool AutoApproveInterviewSchedule = true
);
```

### `HiringApprovalRequest`

```csharp
// src/PMAgent.Application/Models/HiringApprovalRequest.cs
record HiringApprovalRequest(
  bool Approved = true,
  string Comment = ""
);
```

### `HiringCandidateResponseRequest`

```csharp
// src/PMAgent.Application/Models/HiringCandidateResponseRequest.cs
record HiringCandidateResponseRequest(
  string Message,
  bool IsHintRequest = false  // true → return hint keywords instead of advancing the question
);
```

Natural-language hint requests are also detected at runtime, so the flag is optional when the candidate asks for a hint directly in the interview language.

### `HiringSessionResult`

```csharp
// src/PMAgent.Application/Models/HiringSessionResult.cs
record HiringSessionResult(
  Guid SessionId,
  string Stage,
  bool RequiresUserApproval,
  string ApprovalType,
  double ScreeningFitScore,
  double InterviewScore,
  string CurrentSpeaker,
  string CurrentPrompt,
  string StatusSummary,
  string SeniorityLevel,
  string TechnicalInterviewRole,
  string NotesDocumentPath,
  IReadOnlyCollection<string> Participants,
  IReadOnlyCollection<HiringTranscriptTurn> Transcript,
  bool FollowUpAvailable,       // true → a follow-up question is ready after the current answer
  string? PendingFollowUp,      // the follow-up question text, if FollowUpAvailable
  string CandidateFolder        // path to the per-candidate folder created at session start
);
```

### `InterviewQuestion`

```csharp
// src/PMAgent.Application/Models/InterviewQuestion.cs
sealed record InterviewQuestion(
  string Speaker,
  string Text,
  string? FollowUpText,               // optional follow-up asked after the primary answer
  IReadOnlyList<string> HintKeywords  // keywords returned when the candidate requests a hint
)
{
  static InterviewQuestion Simple(string speaker, string text);
  static InterviewQuestion WithFollowUp(string speaker, string text, string followUpText,
                                        IReadOnlyList<string>? hintKeywords = null);
}
```

### `InterviewQuestionTemplate`

```csharp
// src/PMAgent.Application/Models/InterviewQuestionTemplate.cs
sealed record InterviewQuestionTemplate
{
  string Speaker;
  string TextTemplate;
  string? VietnameseTextTemplate;
  string? FollowUpTemplate;
  string? VietnameseFollowUpTemplate;
  List<string> HintKeywords;
  string AppliesToTechnicalRole;
}
```

### `HiringWorkflowSettings`

```csharp
// src/PMAgent.Application/Models/HiringWorkflowSettings.cs
sealed record HiringWorkflowSettings
{
  double ScreeningPassThreshold;
  int HintKeywordCount;
  int MaxCandidateRequestsPerQuestion;
  int GeneralQuestionCount;
  int TechnicalQuestionCount;
  List<InterviewQuestionTemplate> GeneralQuestions;
  List<InterviewQuestionTemplate> TechnicalQuestions;
  List<InterviewQuestionTemplate> BusinessQuestions;
  List<InterviewQuestionTemplate> ClosingQuestions;
  InterviewScoringSettings Scoring;
}
```

The configured question templates now act as fallback structure only. `ConfigurableInterviewQuestionProvider` first asks the LLM to generate a one-shot interview question bank, including one PM opener, multiple technical rounds, and one HR closing question. If the model output is invalid or incomplete, the provider falls back to the configured templates. It also resolves a hiring seniority target from the request or inferred hiring context so question scope stays aligned to `JUNIOR`, `MID`, or `SENIOR` expectations.

Before generating the bank, the provider infers stack priorities fresh from the current project brief and JD, then compares the candidate CV against those priorities. It now prioritizes recognized skills from five categories first: programming languages, frameworks, system design, databases, and agile/scrum delivery methodology. From that, it derives ranked requirement focus, direct overlap, project-critical gaps, and candidate-adjacent strengths. The main bias is toward the current JD-specific requirement focus and the overlap set, so technical questions stay close to this hiring request's actual stack needs rather than drifting toward unrelated CV strengths or a generic cross-job stack profile. Project-critical gaps can still appear, but mainly as transferability questions instead of pure recall checks.

The provider now also accepts a locked interview language (`EN` or `VI`). One-shot question generation is instructed to use that language only, and if the returned question bank does not match it, the provider falls back to configured templates. Those fallback templates can define explicit Vietnamese text and follow-up variants through `VietnameseTextTemplate` and `VietnameseFollowUpTemplate`.

The default interview mix is intentionally stack-heavy:

- `GeneralQuestionCount = 1` to anchor the discussion in the candidate's real experience
- `TechnicalQuestionCount = 8` to spend most evaluated time on project-stack depth
- one HR closing round

At runtime, the same provider still reads the live session markdown notes to do one thing:

- generate a conversational interviewer reply when the candidate asks a question during the interview

This keeps candidate clarification replies grounded in the same notes artifact without re-planning the main interview questions on every turn.

`InMemoryHiringWorkflowService` classifies each candidate turn before scoring. Hint requests route to the hint flow, non-answer interview requests route to a conversational reply flow, and only actual answers enter the evaluation path. `MaxCandidateRequestsPerQuestion` controls how many side requests are tolerated on the same active question before the interviewer adds a focus reminder and explicitly returns the candidate to that question.

### `InterviewScoringSettings`

```csharp
// src/PMAgent.Application/Models/InterviewScoringSettings.cs
sealed record InterviewScoringSettings
{
  double BaseScore;
  double EarlyStopThreshold;
  double SituationQuestionScoreCap;
  int MinimumResponsesBeforeStop;
  int KeywordHitPoints;
  double KeywordHitMax;
  int PositiveSignalPoints;
  double PositiveSignalMax;
  int NegativeSignalPenalty;
  double NegativeSignalMax;
  List<string> PositiveSignals;
  List<string> NegativeSignals;
  List<InterviewRoleSignalGroup> RoleSignals;
  List<InterviewScoringDimensionDefinition> Dimensions;
  List<InterviewSeniorityProfile> SeniorityProfiles;
}
```

The primary scoring path is now fully LLM-first. `Dimensions` defines the evaluation breakdown requested from the model, including names, descriptions, and weights. The default rubric is intentionally broader than keyword matching: `communication`, `problem_solving`, `technical_judgment`, `ownership`, and `collaboration`. `SituationQuestionScoreCap` limits situational or hypothetical answers to 20% of the total evaluation weight, so most of the score must come from real stack evidence and concrete delivery work. The evaluator now scores the latest answer first, returns answer-quality metadata for feedback and follow-up gating, and the workflow aggregates those per-answer scores into the session score. If the LLM output is unavailable or invalid, the fallback applies a conservative latest-answer heuristic instead of a fixed neutral score.

### `InterviewSeniorityProfile`

```csharp
// src/PMAgent.Application/Models/InterviewScoringSettings.cs
sealed record InterviewSeniorityProfile
{
  string Level;
  string Summary;
  List<string> ExpectedBehaviors;
  string ScoreGuidance;
}
```

These profiles define how the evaluator should calibrate expectations for junior, mid, and senior candidates. The resolved `SeniorityLevel` is persisted in `HiringSessionResult`, added to generated notes and per-candidate artifacts, and reused by the screening scorer, interview question provider, and interview scorer so the workflow applies one consistent level assumption end to end.

### `HiringFitAssessmentResult`

```csharp
// src/PMAgent.Application/Models/HiringFitAssessmentResult.cs
sealed record HiringFitAssessmentResult(
  double Score,
  bool ShouldAdvance,
  string Summary,
  IReadOnlyCollection<string> Strengths,
  IReadOnlyCollection<string> Gaps);
```

Returned by the HR screening scorer. Replaces the earlier keyword-match-only view with a semantic assessment summary plus explicit strengths and gaps.

### `InterviewScoreDimension`

```csharp
// src/PMAgent.Application/Models/InterviewScoreDimension.cs
sealed record InterviewScoreDimension(
  string Name,
  double Score,
  string Summary);
```

### `InterviewScoreResult`

```csharp
// src/PMAgent.Application/Models/InterviewScoreResult.cs
sealed record InterviewScoreResult(
  double Score,
  bool ShouldStop,
  string Rationale,
  IReadOnlyCollection<InterviewScoreDimension>? Dimensions = null,
  string Feedback = "",
  string AnswerQuality = "PARTIAL",
  bool ShouldAskFollowUp = false);
```

The interview scorer now returns the score of the latest answer, an optional dimension breakdown, interviewer feedback text, an answer-quality label, and whether the workflow should ask a follow-up. `InMemoryHiringWorkflowService` keeps a running average of answer scores as the session-level interview score.

At runtime, `InMemoryHiringWorkflowService` also adds a short interviewer feedback turn after each accepted candidate answer. That feedback is appended to the transcript and the live Q&A log so the interview feels more like a guided conversation instead of a silent score update. Runtime rendering no longer prefixes those turns with `Feedback:`, so the transcript reads like a normal interviewer response rather than an evaluator annotation.

---

## Interfaces

### `IAgentExecutor`

```csharp
// src/PMAgent.Application/Abstractions/IAgentExecutor.cs
Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken ct);
```

Implemented by `AgentExecutor` in the Infrastructure layer. Drives the Think → Action → Input → Output loop.

### `IAgentTool`

```csharp
// src/PMAgent.Application/Abstractions/IAgentTool.cs
string Name { get; }         // Unique key used to look up the tool in AgentExecutor
string Description { get; }  // Human-readable purpose
Task<string> ExecuteAsync(string input, CancellationToken ct);
```

Implement this interface to add any new capability to the agent.

### `ISpecializedAgent`

```csharp
// src/PMAgent.Application/Abstractions/ISpecializedAgent.cs
string Role { get; }        // Role token, e.g. "PO", "PM", "BA", "DEV", "TEST"
string Description { get; } // Human-readable purpose
Task<AgentTaskResult> ExecuteAsync(AgentTask task, CancellationToken ct);
```

Implement this interface (or extend `SpecializedAgentBase`) to add a new delivery-team role to the orchestrator.

### `SpecializedAgentBase`

```csharp
// src/PMAgent.Infrastructure/Agents/SpecializedAgentBase.cs
public abstract class SpecializedAgentBase : ISpecializedAgent
```

Abstract base class that centralises the shared logic for all LLM-backed agents:
- Holds `ILlmClient _llm` (injected via constructor)
- Implements `ExecuteAsync` — calls `_llm.CompleteAsync(SystemPrompt, userMessage)`
- Implements `BuildUserMessage` (private) — combines `task.Goal` and `task.Context` into the user turn

Concrete agents only need to override `Role`, `Description`, and `SystemPrompt`.

### `IOrchestratorAgent`

```csharp
// src/PMAgent.Application/Abstractions/IOrchestratorAgent.cs
Task<OrchestrationResult> RunAsync(OrchestrationRequest request, CancellationToken ct);
```

Implemented by `OrchestratorAgent`. Coordinates all `ISpecializedAgent` instances in sequence.

### `IAgentRoutingPolicy`

```csharp
// src/PMAgent.Application/Abstractions/IAgentRoutingPolicy.cs
IReadOnlyList<string> BuildInitialRoute(OrchestrationRequest request);
bool ShouldEarlyStop(IReadOnlyCollection<AgentTaskResult> completedResults);
bool ShouldFallbackToFullChain(IReadOnlyCollection<AgentTaskResult> completedResults);
```

Implemented by `RuleBasedAgentRoutingPolicy`. Provides the baseline routing matrix and transition guards (early-stop and fallback). Used as a fallback when LLM-driven routing is unavailable or returns an unparseable response.

### `IAgentMemory`

```csharp
// src/PMAgent.Application/Abstractions/IAgentMemory.cs
void Record(string role, string content);
string BuildContext();
IReadOnlyList<MemoryEntry> Entries { get; }
```

Implemented by `InMemoryAgentMemory`. Maintains a structured append-only log of all role outputs for a single agent run. `AgentExecutor` uses it to build the accumulated context passed to each Think step, replacing manual string concatenation. `HiringOrchestrationAgent` uses it to persist hiring assessments across reasoning phases.

```csharp
// src/PMAgent.Application/Abstractions/IAgentMemory.cs
sealed record MemoryEntry(string Role, string Content, DateTimeOffset RecordedAt);
```

### `IAgentPlanner`

```csharp
// src/PMAgent.Application/Abstractions/IAgentPlanner.cs
Task<PlanningResponse> BuildPlanAsync(PlanningRequest request, CancellationToken ct);
```

Implemented by `LlmAgentPlanner` (default, registered in DI). Calls `ILlmClient` with `ProjectName`, `Goal`, `TeamMembers`, and `Constraints` to produce a context-aware plan. Expects JSON output `{summary, nextActions[], risks[]}` and has a deterministic fallback. `RuleBasedAgentPlanner` still exists for backward-compatible tests but is no longer the default registration.

### `IHiringWorkflowService`

```csharp
// src/PMAgent.Application/Abstractions/IHiringWorkflowService.cs
Task<HiringSessionResult> StartAsync(HiringSessionStartRequest request, CancellationToken ct);
Task<HiringSessionResult?> GetAsync(Guid sessionId, CancellationToken ct);
Task<HiringSessionResult> ApproveScreeningAsync(Guid sessionId, HiringApprovalRequest request, CancellationToken ct);
Task<HiringSessionResult> ApproveInterviewScheduleAsync(Guid sessionId, HiringApprovalRequest request, CancellationToken ct);
Task<HiringSessionResult> SubmitCandidateResponseAsync(Guid sessionId, HiringCandidateResponseRequest request, CancellationToken ct);
Task<HiringSessionResult> RequestHintAsync(Guid sessionId, CancellationToken ct);
```

Implemented by `InMemoryHiringWorkflowService`. Coordinates the staged hiring process: HR screening, user approvals, interview turns, scoring, per-candidate folder creation, live Q&A file logging, notes-backed dynamic question generation, follow-up questions, clarification handling, and hint delivery.

#### Per-candidate folder

At session start, `InMemoryHiringWorkflowService` creates a folder `candidate-{sessionId}/` in the working directory and writes three files:

| File | Contents |
|---|---|
| `jd-keywords.md` | Keywords extracted from the job description |
| `cv-keywords.md` | Keywords extracted from the candidate CV |
| `interview-qa.md` | Live Q&A log — appended in real-time as each question and answer is exchanged |

The `CandidateFolder` field of `HiringSessionResult` gives the caller the full path to this folder.

The service also stores the semantic HR screening summary, strengths, and gaps in session state so the approval and transcript flow can reference the model's assessment instead of only a numeric threshold.

#### Follow-up questions

Each `InterviewQuestion` may carry an optional `FollowUpText`. After the candidate answers a primary question, the service checks whether a follow-up should be asked before advancing to the next panel question. At most one follow-up is asked per question (`FollowUpAsked` flag). The follow-up text is surfaced in `HiringSessionResult.PendingFollowUp` and `FollowUpAvailable`.

The live interview no longer depends on prebuilding a full question pack up front. Before each new interviewer turn, `InMemoryHiringWorkflowService` writes the current `hiring-session-{sessionId}.md` notes file and asks `ConfigurableInterviewQuestionProvider` to generate exactly one next question from that markdown file. This lets the provider reuse the JD, CV, transcript, HR notes, and prior evaluation context already written to disk instead of resending raw JD/CV payloads every time a new question is needed. Fallback templates still exist for resilience if the model output is invalid.

#### Clarification handling

If a candidate message ends with `?` or contains a clarification phrase, the service generates an interviewer reply using JD keywords and project brief context, and keeps the same question active without advancing.

#### Hint delivery

When `HiringCandidateResponseRequest.IsHintRequest = true`, or when `POST /api/hiring/sessions/{sessionId}/hint` is called, the service returns 2–3 hint keywords from `InterviewQuestion.HintKeywords` (or derived from JD keywords) without advancing the question.

#### Scoring rubric

The interview scoring system now uses a configurable rubric stored under `HiringWorkflow:Scoring` in `appsettings.json`, but the primary evaluation path is semantic LLM scoring.

The rubric controls:

- early-stop threshold
- minimum number of responses before stopping
- scoring dimensions requested from the LLM (`communication`, `problem_solving`, `technical_judgment`, `ownership`, `collaboration` by default)
- prompt-level guidance supplied to the evaluator model, including explicit instructions not to reward keyword overlap on its own, to mirror the candidate-facing language when writing rationales, and to calibrate expectations by resolved seniority

`LlmInterviewScoringAgent` asks the model to return a numeric score, stop/no-stop decision, rationale, and per-dimension breakdown. `RuleBasedInterviewScoringAgent` remains only as a conservative non-evaluating fallback when the LLM response is unavailable or malformed.

### `IHiringFitScoringAgent`

```csharp
// src/PMAgent.Application/Abstractions/IHiringFitScoringAgent.cs
Task<HiringFitAssessmentResult> EvaluateAsync(
  string projectBrief,
  string jobDescription,
  string candidateCv,
  string targetSeniority,
  string technicalInterviewRole,
  CancellationToken cancellationToken = default);
```

Implemented by `LlmHiringFitScoringAgent`. Performs semantic CV/JD fit scoring during the HR screening gate and returns a decision, summary, strengths, and gaps.

### `IInterviewScoringAgent`

```csharp
// src/PMAgent.Application/Abstractions/IInterviewScoringAgent.cs
Task<InterviewScoreResult> EvaluateAsync(
  string projectBrief,
  string jobDescription,
  string targetSeniority,
  string technicalInterviewRole,
  IReadOnlyCollection<HiringTranscriptTurn> transcript,
  int candidateResponseCount,
  CancellationToken cancellationToken = default);
```

Implemented by `LlmInterviewScoringAgent`, with `RuleBasedInterviewScoringAgent` as a conservative fallback. Produces a running interview score, dimension breakdown, and stop/no-stop decision using the configurable `HiringWorkflow:Scoring` rubric.

### `IInterviewQuestionProvider`

```csharp
// src/PMAgent.Application/Abstractions/IInterviewQuestionProvider.cs
Task<IReadOnlyList<InterviewQuestion>> BuildQuestionsAsync(
  HiringSessionStartRequest request,
  string technicalInterviewRole,
  CancellationToken cancellationToken = default);

Task<InterviewQuestion> BuildQuestionFromNotesAsync(
  HiringSessionStartRequest request,
  string technicalInterviewRole,
  string speaker,
  int questionNumber,
  string sessionNotesPath,
  CancellationToken cancellationToken = default);
```

Implemented by `ConfigurableInterviewQuestionProvider`. `BuildQuestionsAsync` still supports generating a full staged pack when needed, while `BuildQuestionFromNotesAsync` is the runtime path used by the hiring workflow to generate the next interviewer question from the live markdown notes file for the session.

---

## Built-in Tools

Each tool now calls `ILlmClient` with a focused system prompt for its domain, producing context-aware output that depends on the actual goal and accumulated reasoning context.

| Tool name | Class | File |
|---|---|---|
| `scope_analysis` | `ScopeAnalysisTool` | `src/PMAgent.Infrastructure/Tools/ScopeAnalysisTool.cs` |
| `risk_assessment` | `RiskAssessmentTool` | `src/PMAgent.Infrastructure/Tools/RiskAssessmentTool.cs` |
| `action_planner` | `ActionPlannerTool` | `src/PMAgent.Infrastructure/Tools/ActionPlannerTool.cs` |

### How to add a new tool

1. Create a class implementing `IAgentTool` in `src/PMAgent.Infrastructure/Tools/`. Inject `ILlmClient` and define a `SystemPrompt` constant.
2. Register it in `DependencyInjection.cs`:
   ```csharp
   services.AddScoped<IAgentTool, YourNewTool>();
   ```
3. The LLM-driven Think step in `AgentExecutor` will automatically include the new tool in its system prompt and may invoke it when reasoning about the goal.
4. **Update `docs/technical.md`** — add a row to the Built-in Tools table.
5. **Update `docs/business.md`** — if the tool adds user-visible behaviour, describe it in the Capabilities section.

---

## Specialized Agents

The orchestrator pattern adds six role-specific agents. All six extend `SpecializedAgentBase`, which centralises the shared logic:
- Constructor injection of `ILlmClient` and `ILogger`
- `BuildUserMessage` — formats the goal + accumulated context into a single user prompt
- `ExecuteAsync` — calls `ILlmClient.CompleteAsync` and wraps the result in `AgentTaskResult`

Each concrete agent only declares three things:

```csharp
// src/PMAgent.Infrastructure/Agents/SpecializedAgentBase.cs
public abstract class SpecializedAgentBase : ISpecializedAgent
{
  protected SpecializedAgentBase(ILlmClient llm, ILogger logger)
  {
    _llm = llm;
    _logger = logger;
  }

    public abstract string Role { get; }
    public abstract string Description { get; }
    protected abstract string SystemPrompt { get; }   // role-specific LLM instruction

    // ExecuteAsync and BuildUserMessage are implemented once here
}
```

| Role | Class | Primary Deliverable |
|---|---|---|
| `PO` | `ProductOwnerAgent` | Product vision, goals, user stories, acceptance criteria |
| `PM` | `ProjectManagerAgent` | Milestones, timeline, resource plan, risk register |
| `HR` | `HrAgent` | Hiring plan, staffing strategy, interview process, onboarding checkpoints |
| `BA` | `BusinessAnalystAgent` | Functional requirements, use cases, gap analysis |
| `DEV` | `DeveloperAgent` | Tech stack, architecture, API design, implementation approach |
| `TEST` | `TesterAgent` | Test plan, test types, quality gates, coverage targets |
| `HIRING_ORC` | `HiringOrchestrationAgent` | End-to-end hiring assessment (CV analysis, JD fit, interview plan, recommendation) using `IAgentMemory` |

All agents live in `src/PMAgent.Infrastructure/Agents/`.

### Orchestrator dispatch sequence

Dispatch is now LLM-driven with a rule-based fallback:

- The orchestrator asks `IAgentRoutingPolicy` for an initial route.
- After each agent completes, the orchestrator asks the `ILlmClient` what role should run next (`continue`, `stop`, or `escalate`).
- If the LLM response is unavailable or unparseable, it falls back to rule-based guards: early-stop when a high-confidence `stop` decision is produced, or fallback to full-chain when confidence drops, escalation is requested, or an agent fails.

```
OrchestratorAgent.RunAsync(OrchestrationRequest)
│
├── route = IAgentRoutingPolicy.BuildInitialRoute(request)
├── while route has pending roles
│   ├── dispatch AgentTask(role, brief, accumulatedContext)
│   ├── append output to accumulatedContext
│   ├── ask ILlmClient: "what role should run next?"
│   │   ├── LLM says "stop"      → break loop
│   │   ├── LLM says "escalate"  → enqueue missing full-chain roles
│   │   ├── LLM says "continue"  → enqueue nextRole if not yet run
│   │   └── LLM unavailable/invalid → rule-based fallback (ShouldFallbackToFullChain / ShouldEarlyStop)
│   └── repeat
└── builds OrchestrationResult(Summary, AgentOutputs)
```

### How to add a new specialized agent

1. Create a class in `src/PMAgent.Infrastructure/Agents/` that extends `SpecializedAgentBase`:
   ```csharp
   public sealed class YourNewAgent : SpecializedAgentBase
   {
       public YourNewAgent(ILlmClient llm) : base(llm) { }

       public override string Role => "ROLE_TOKEN";
       public override string Description => "One-line description.";
       protected override string SystemPrompt => "Your role-specific LLM instruction...";
   }
   ```
2. Register it in `DependencyInjection.cs`:
   ```csharp
   services.AddScoped<ISpecializedAgent, YourNewAgent>();
   ```
3. Add the role token to the full-chain order in `RuleBasedAgentRoutingPolicy` (and keep `OrchestratorAgent.AgentOrder` aligned for fallback queueing).
4. **Update `docs/technical.md`** — add a row to the Specialized Agents table.
5. **Update `docs/business.md`** — describe the new role in the Key Capabilities section.

---

## `AgentExecutor` — Internal Flow

```
AgentExecutor(IEnumerable<IAgentTool> tools, ILogger logger, ILlmClient? llm = null)
│  Builds: Dictionary<string, IAgentTool>  (case-insensitive by tool Name)
│  Creates: InMemoryAgentMemory for the run
│
└── RunAsync(AgentRunRequest request)
      │
      ├── memory.Record("CONTEXT", request.Context)
      │
      └── for iteration in [0, MaxIterations)
            │
            ├── ThinkAsync(goal, memory.BuildContext(), completedSteps)
            │     ├── if ILlmClient available:
            │     │     → call LLM with ReAct system prompt + step history
            │     │     → parse JSON: {thought, action, actionInput} or {thought, isFinal}
            │     │     → on error: fall back to rule-based
            │     └── ThinkRuleBased: scope_analysis → risk_assessment → action_planner → finalize
            │
            ├── if isFinal
            │     actionOutput = BuildFinalAnswer(goal, completedSteps)
            │
            ├── else if tool found in dictionary
            │     actionOutput = await tool.ExecuteAsync(actionInput)
            │
            ├── else
            │     actionOutput = "Tool '{action}' not found. Skipping."
            │
            ├── steps.Add(new AgentStep(thought, action, input, output, isFinal))
            │
            ├── memory.Record(action.ToUpperInvariant(), actionOutput)
            │
            └── if isFinal → break
      │
      └── return AgentRunResult(finalAnswer, steps)
```

---

## API Reference

### Browser entry points

| Path | Purpose |
|---|---|
| `/` | Static chat UI for interacting with the orchestrator in the browser, including markdown output rendering, local history, presets, and sample payload preview |
| `/swagger` | Swagger UI for inspecting and testing the HTTP API |
| `/health` | Health-check endpoint |

### `POST /api/agent/run`

**Request**

```json
{
  "goal":          "Ship the MVP for the PM dashboard",
  "context":       "Team size: 4 engineers, deadline: 8 weeks",
  "maxIterations": 10
}
```

| Field | Type | Required | Constraints |
|---|---|---|---|
| `goal` | string | yes | Non-empty |
| `context` | string | no | Free text; seeds the reasoning context |
| `maxIterations` | int | no | 1–50; default: 10 |

**Response — `200 OK`**

```json
{
  "finalAnswer": "Agent completed planning for goal: 'Ship the MVP...'.\n\n...",
  "steps": [
    {
      "thought":      "I need to understand the scope of the goal...",
      "action":       "scope_analysis",
      "actionInput":  "Ship the MVP for the PM dashboard",
      "actionOutput": "Scope for '...': Deliver a working system...",
      "isFinal":      false
    },
    {
      "thought":      "Scope is clear. Now I need to identify potential risks.",
      "action":       "risk_assessment",
      "actionInput":  "...",
      "actionOutput": "Identified risks: (1) Scope creep...",
      "isFinal":      false
    },
    {
      "thought":      "I have the scope and risks. Now I can create a plan.",
      "action":       "action_planner",
      "actionInput":  "...",
      "actionOutput": "Action plan: (1) Clarify acceptance criteria...",
      "isFinal":      false
    },
    {
      "thought":      "I have gathered sufficient information. Producing final answer.",
      "action":       "finalize",
      "actionInput":  "...",
      "actionOutput": "Agent completed planning for goal: ...",
      "isFinal":      true
    }
  ]
}
```

**Error responses**

| Status | Condition |
|---|---|
| `400 Bad Request` | `goal` is empty or whitespace |
| `400 Bad Request` | `maxIterations` is outside 1–50 |

### `POST /api/orchestrator/run`

**Request**

```json
{
  "projectBrief":          "Build a SaaS project management tool for remote teams",
  "context":               "Start-up phase, team of 5, 10-week runway",
  "maxIterationsPerAgent": 10,
  "workflow":              "delivery"
}
```

| Field | Type | Required | Constraints |
|---|---|---|---|
| `projectBrief` | string | yes | Non-empty |
| `context` | string | no | Free text; seeds the reasoning context |
| `maxIterationsPerAgent` | int | no | 1–50; default: 10 |
| `workflow` | string | no | `delivery` or `hiring`; default: `delivery` |
| `jobDescription` | string | conditional | Required when `workflow = hiring` |
| `candidateCv` | string | conditional | Required when `workflow = hiring` |
| `technicalInterviewRoles` | string[] | no | Only `DEV` and/or `TEST` |

**Response — `200 OK`**

```json
{
  "summary": "# Project Orchestration Summary\n\n**Brief:** Build a SaaS...\n...",
  "agentOutputs": [
    {
      "role": "PO",
      "output": "## Product Owner Output...",
      "success": true,
      "decision": "continue",
      "confidence": 0.8,
      "issues": [],
      "nextAction": "continue"
    },
    {
      "role": "PM",
      "output": "## Project Manager Output...",
      "success": true,
      "decision": "continue",
      "confidence": 0.8,
      "issues": [],
      "nextAction": "continue"
    }
  ]
}
```

**Error responses**

| Status | Condition |
|---|---|
| `400 Bad Request` | `projectBrief` is empty or whitespace |
| `400 Bad Request` | `maxIterationsPerAgent` is outside 1–50 |
| `400 Bad Request` | `workflow` is not `delivery` or `hiring` |
| `400 Bad Request` | `jobDescription` or `candidateCv` is missing in hiring mode |
| `400 Bad Request` | `technicalInterviewRoles` contains unsupported role values |

### `POST /api/hiring/sessions`

Starts a staged hiring session. HR screens the CV first and asks for user approval before forwarding the CV to PM, BA, and the selected technical interviewer.

**Request**

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

### `POST /api/hiring/sessions/{sessionId}/approve-screening`

Moves the workflow forward after HR screening. If approved, PM prepares the interview schedule; if `autoApproveInterviewSchedule = true`, the interview starts immediately.

### `POST /api/hiring/sessions/{sessionId}/approve-interview`

Used only when the PM schedule requires explicit user approval before the interview starts.

### `POST /api/hiring/sessions/{sessionId}/candidate-response`

Submits the candidate's answer during the interview. The workflow then records HR notes, updates the interview score, and advances to the next interviewer or ends the interview.

### `GET /api/hiring/sessions/{sessionId}`

Returns the current hiring session state, approvals, transcript, score, and notes document path.

### `GET /api/hiring/sessions/{sessionId}/notes`

Returns the generated markdown interview notes file when the session has already produced one. This is the endpoint used by the browser UI for notes export.

### `POST /api/hiring/sessions/{sessionId}/hint`

Requests hint keywords for the candidate's currently active interview question. The service returns 2–3 hint keywords without advancing the question. This is equivalent to submitting a `candidate-response` with `IsHintRequest = true`.

### `POST /api/harness/run`

Runs all registered harness scenarios and returns the full `HarnessReport` as JSON. The report is also written to `harness-reports/` on disk. No request body is required.

### `POST /api/harness/run/{scenarioId}`

Runs a single harness scenario by ID. Returns `HarnessScenarioResult` on `200 OK`, or `404 Not Found` if the scenario ID is unknown.

### Browser entrypoint `/`

The root path serves the static browser client from `src/PMAgent.Api/wwwroot/`.

The browser client supports two interaction modes:

- one-shot delivery orchestration through `/api/orchestrator/run`
- staged hiring sessions through `/api/hiring/sessions`

The hiring console keeps the active session in browser storage, renders a transcript timeline, exports HR notes through `/api/hiring/sessions/{sessionId}/notes`, and can prefill JD/CV inputs from uploaded text files.

### `POST /api/planning/next-actions` _(legacy)_

Calls `RuleBasedAgentPlanner` (no longer the default DI registration — `LlmAgentPlanner` is used by default, but the legacy class remains for backward-compatible tests). Returns a flat `PlanningResponse` (summary, next actions, risks). Does not use the agent loop.

---

## LLM Provider

All agents share a single `ILlmClient` abstraction. The concrete implementation is selected at startup based on `LlmSettings:Provider` in `appsettings.json`.

| Provider value | Implementation class | When to use |
|---|---|---|
| `OpenAI` _(default)_ | `OpenAiLlmClient` | Production — requires `ApiKey` |
| `Ollama` | `OllamaLlmClient` | Local development — zero cost, runs on local hardware |

### Configuration

```json
// appsettings.json  (production default)
"LlmSettings": {
  "Provider": "OpenAI",
  "ApiKey":   "<your-openai-api-key>",
  "Model":    "gpt-4o",
  "OllamaBaseUrl": "http://localhost:11434",
  "OllamaModel":   "llama3.2"
}
```

```json
// appsettings.Development.json  (local development — overrides Provider only)
"LlmSettings": {
  "Provider":      "Ollama",
  "OllamaBaseUrl": "http://localhost:11434",
  "OllamaModel":   "llama3.2"
}
```

### LlmSettings model

```csharp
// src/PMAgent.Application/Models/LlmSettings.cs
public enum LlmProvider { OpenAI, Ollama }

public sealed class LlmSettings
{
    public LlmProvider Provider     { get; init; } = LlmProvider.OpenAI;
    // OpenAI
    public string ApiKey            { get; init; } = string.Empty;
    public string Model             { get; init; } = "gpt-4o";
    // Ollama
    public string OllamaBaseUrl     { get; init; } = "http://localhost:11434";
    public string OllamaModel       { get; init; } = "llama3.2";
}
```

### How DI selects the provider

```csharp
// src/PMAgent.Infrastructure/DependencyInjection.cs
if (llmSettings.Provider == LlmProvider.Ollama)
    services.AddScoped<ILlmClient, OllamaLlmClient>();
else
    services.AddScoped<ILlmClient, OpenAiLlmClient>();
```

### Adding a new LLM provider

1. Create a class implementing `ILlmClient` in `src/PMAgent.Infrastructure/`.
2. Add a new value to the `LlmProvider` enum in `LlmSettings.cs`.
3. Add a `case` branch in `DependencyInjection.cs`.
4. Update this table above.

### Setting up Ollama locally

```bash
# 1. Install Ollama  →  https://ollama.com/download
# 2. Pull the model you want to use
ollama pull llama3.2
# 3. Verify it is running
ollama list
# 4. Start the API — Development profile automatically uses Ollama
dotnet run --project src/PMAgent.Api --launch-profile Development
```

---

## Dependency Injection

```csharp
// src/PMAgent.Api/Program.cs
builder.Services.AddInfrastructure(builder.Configuration);
```

```csharp
// src/PMAgent.Infrastructure/DependencyInjection.cs
var healthChecks = services.AddHealthChecks();
var hiringWorkflowSettings = configuration.GetSection("HiringWorkflow").Get<HiringWorkflowSettings>()
  ?? HiringWorkflowSettings.CreateDefault();

// ILlmClient — provider selected at startup from LlmSettings:Provider
if (llmSettings.Provider == LlmProvider.Ollama)
{
    services.AddScoped<ILlmClient, OllamaLlmClient>();  // local / zero-cost
  healthChecks.AddCheck<OllamaHealthCheck>("ollama", tags: ["ready"]);
}
else
    services.AddScoped<ILlmClient, OpenAiLlmClient>();  // production

services.AddScoped<IAgentPlanner, LlmAgentPlanner>();     // LLM-backed planner
services.AddScoped<IAgentRoutingPolicy, RuleBasedAgentRoutingPolicy>(); // baseline dynamic route rules
services.AddSingleton(hiringWorkflowSettings);
services.AddScoped<IInterviewQuestionProvider, ConfigurableInterviewQuestionProvider>();
services.AddScoped<IHiringFitScoringAgent, LlmHiringFitScoringAgent>();
services.AddScoped<IInterviewScoringAgent, LlmInterviewScoringAgent>();
services.AddScoped<IHiringWorkflowService, InMemoryHiringWorkflowService>();

services.AddTransient<IAgentMemory, InMemoryAgentMemory>();   // fresh instance per agent

services.AddScoped<IAgentTool, ScopeAnalysisTool>();          // registered as IAgentTool
services.AddScoped<IAgentTool, RiskAssessmentTool>();          // all resolved together
services.AddScoped<IAgentTool, ActionPlannerTool>();           // by IEnumerable<IAgentTool>

services.AddScoped<IAgentExecutor, AgentExecutor>();

services.AddScoped<ISpecializedAgent, ProductOwnerAgent>();   // registered as ISpecializedAgent
services.AddScoped<ISpecializedAgent, ProjectManagerAgent>();  // all resolved together
services.AddScoped<ISpecializedAgent, BusinessAnalystAgent>(); // by IEnumerable<ISpecializedAgent>
services.AddScoped<ISpecializedAgent, DeveloperAgent>();
services.AddScoped<ISpecializedAgent, TesterAgent>();
services.AddScoped<ISpecializedAgent, HiringOrchestrationAgent>(); // LLM-driven hiring agent

services.AddScoped<IOrchestratorAgent, OrchestratorAgent>();

// Harness layer
services.AddSingleton<IHarnessScenarioProvider, DefaultHarnessScenarioProvider>();
services.AddScoped<IHarnessAssertionEngine, HarnessAssertionEngine>();
services.AddScoped<IHarnessReportSink, JsonHarnessReportSink>();
services.AddScoped<IHarnessReportSink, MarkdownHarnessReportSink>();
services.AddScoped<IHarnessRunner, HarnessRunner>();
```

Health check services are always registered because the API maps `/health` in every environment. The Ollama-specific probe is added only when `LlmSettings.Provider = Ollama`.

`AgentExecutor` receives all `IAgentTool` registrations via constructor injection as `IEnumerable<IAgentTool>`, plus the shared `ILlmClient` for LLM-driven Think steps. `OrchestratorAgent` uses the same pattern with `IEnumerable<ISpecializedAgent>` and receives `ILlmClient` for LLM-driven routing decisions after each agent completes. Adding a new agent requires only one `AddScoped` line.

`IAgentMemory` is registered as `Transient` so each component that injects it (e.g. `HiringOrchestrationAgent`) receives its own fresh memory instance, preventing state leakage between parallel requests.

The hiring workflow now also receives a singleton `HiringWorkflowSettings` object bound from configuration. This centralises fallback interview templates, semantic scoring dimensions, and HR screening thresholds in one place.

---

## Harness Layer

The harness layer provides deterministic scenario-based validation of the agent system without requiring a real LLM. It is designed for fast CI runs and can be extended for nightly LLM-connected tests.

### Interfaces (`PMAgent.Application/Abstractions/Harness/`)

| Interface | Responsibility |
|---|---|
| `IHarnessScenarioProvider` | Returns the catalog of `HarnessScenario` definitions (`GetScenarios()`) |
| `IHarnessRunner` | Drives the agent system through all or a single scenario (`RunAllAsync`, `RunScenarioAsync(scenarioId)`) |
| `IHarnessAssertionEngine` | Evaluates a scenario's output against expected sections, decision, and confidence |
| `IHarnessReportSink` | Persists a completed `HarnessReport` (multiple sinks can be registered) |

### Models (`PMAgent.Application/Models/Harness/`)

| Model | Key fields |
|---|---|
| `HarnessScenario` | `Id`, `ProjectBrief`, `ExpectedSections`, `ExpectedDecision`, `ExpectedMinConfidence`, `SimulateEmptyLlmResponse`, `SimulateLlmFault` |
| `HarnessAssertion` | `Name`, `Status` (`Pass`/`Fail`/`Skipped`), `Message` |
| `HarnessScenarioResult` | `ScenarioId`, `Passed`, `CorrelationId`, `RoleResults`, `Assertions`, `ErrorMessage`, `DurationMs` |
| `HarnessReport` | `RunId`, `RunAt`, `Scenarios`, `PassRatePercent`, `TotalDurationMs` |

### Infrastructure (`PMAgent.Infrastructure/Harness/`)

| Class | Responsibility |
|---|---|
| `DefaultHarnessScenarioProvider` | 7 built-in scenarios (see below) |
| `HarnessAssertionEngine` | Checks: output non-empty, all expected headings present (case-insensitive, `##`/`#`/`###` variants), valid decision, confidence in `[0.0 .. 1.0]` |
| `HarnessRunner` | Drives `IOrchestratorAgent` per scenario; per-scenario `Stopwatch`; `CorrelationId` per run; fault-exception scenarios caught and marked Pass; writes to all registered `IHarnessReportSink`s |
| `JsonHarnessReportSink` | Writes `harness-reports/harness-{runId}.json` |
| `MarkdownHarnessReportSink` | Writes `harness-reports/harness-{runId}.md` with emoji status, role tables, and failed assertion list |

### Built-in scenarios

| Scenario ID | Input type | What it validates |
|---|---|---|
| `delivery-happy` | Normal project brief | Full delivery chain outputs all expected sections |
| `delivery-ambiguous` | Vague/generic brief | Agent still produces decision and confidence |
| `delivery-edge` | Minimal one-word brief | Graceful handling of minimal input |
| `fault-empty-llm` | Normal brief + `SimulateEmptyLlmResponse = true` | Assertion engine catches missing output |
| `fault-llm-exception` | Normal brief + `SimulateLlmFault = true` | Runner catches exception and passes the scenario |
| `hiring-happy` | Hiring brief with matching CV | Hiring route produces full interview pack |
| `hiring-below-threshold` | Hiring brief with low-fit CV | Low-fit CV produces rejection result |

### Report files

Reports are written to `harness-reports/` in the working directory:

```
harness-reports/
├── harness-{runId}.json   → machine-readable, suitable for CI artifact upload
└── harness-{runId}.md     → human-readable with emoji status and role summary tables
```

### Harness API endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/harness/run` | Runs all scenarios and returns the `HarnessReport` |
| `POST` | `/api/harness/run/{scenarioId}` | Runs a single scenario by ID and returns `HarnessScenarioResult`; `404` if unknown |

### Fast hiring harness coverage

In addition to orchestrator-level harness scenarios, `tests/PMAgent.Tests/HiringHarnessTests.cs` exercises the staged `IHiringWorkflowService` directly under `Category=Harness`. This fast suite covers:

- Stage transitions from screening approval to interview start.
- Full-panel interview completion and notes generation.
- Follow-up question activation before advancing to the next role.
- Hint delivery without advancing the active question.
- Clarification replies that keep the same speaker/question active.
- Early-stop behavior and persisted interview artifacts.

---

## CI Workflow

The pipeline is defined in `.github/workflows/ci.yml` and has two jobs:

| Job | Trigger | Filter | LLM needed | Fail condition |
|---|---|---|---|---|
| `fast` | Every push + every PR | `Category=Harness` | No | Any test failure |
| `nightly` | Daily at 02:00 UTC + `workflow_dispatch` | `Category=HarnessLLM` | Yes (`OPENAI_API_KEY` secret) | Any test failure **or** `passRatePercent < 95` |

Both jobs upload test results (`.trx`) and harness reports (`.json` + `.md`) as build artifacts.

The nightly job reads `LlmSettings__Provider` and `LlmSettings__ApiKey` from GitHub Actions secrets via environment variables, so no secrets are stored in source code.

---

## Running the Solution

```bash
# Start the API (HTTP on port configured in launchSettings.json)
dotnet run --project src/PMAgent.Api

# Run all tests
dotnet test PMAgent.slnx

# Run only harness tests (no LLM required — fast CI mode)
dotnet test --filter "Category=Harness"

# Run LLM-connected harness tests locally (requires a key or Ollama)
LlmSettings__Provider=OpenAI LlmSettings__ApiKey=sk-... dotnet test --filter "Category=HarnessLLM"
```

---

## Test Coverage

| Test class | Test | What is validated |
|---|---|---|
| `RuleBasedAgentPlannerTests` | `BuildPlanAsync_ReturnsActionsAndRisks` | Legacy planner returns actions and risks |
| `AgentExecutorTests` | `RunAsync_ProducesIsFinalStep` | At least one step has `IsFinal = true` (tools injected with `FakeEchoLlmClient`) |
| `AgentExecutorTests` | `RunAsync_LastStepIsAlwaysFinal` | The last step is always the final step |
| `AgentExecutorTests` | `RunAsync_AllThreeToolsAreInvoked` | All three built-in tools execute; rule-based fallback used when LLM returns non-JSON |
| `AgentExecutorTests` | `RunAsync_EachStepHasNonEmptyThoughtAndOutput` | Every step has non-empty `Thought` and `ActionOutput` |
| `AgentExecutorTests` | `RunAsync_FinalAnswerContainsGoal` | `FinalAnswer` includes the original goal string |
| `AgentExecutorTests` | `RunAsync_RespectsMaxIterationsGuard` | `MaxIterations = 1` produces exactly one step |
| `OrchestratorAgentTests` | `RunAsync_RunsAllSixAgents` | `AgentOutputs.Count == 6` for full-chain scenarios |
| `OrchestratorAgentTests` | `RunAsync_OutputContainsAllRoles` | PO/PM/HR/BA/DEV/TEST role tokens are present in outputs |
| `OrchestratorAgentTests` | `RunAsync_AllAgentsReportSuccess` | Every `AgentTaskResult.Success == true` |
| `OrchestratorAgentTests` | `RunAsync_SummaryIsNotEmpty` | `Summary` is non-empty |
| `OrchestratorAgentTests` | `RunAsync_PO_OutputContainsBrief` | PO output includes the project brief string |
| `OrchestratorAgentTests` | `RunAsync_PM_OutputContainsMilestones` | PM output contains "Milestone" |
| `OrchestratorAgentTests` | `RunAsync_HR_OutputContainsHiringPlan` | HR output contains "Hiring Plan" |
| `OrchestratorAgentTests` | `RunAsync_BA_OutputContainsRequirements` | BA output contains "Requirement" |
| `OrchestratorAgentTests` | `RunAsync_DEV_OutputContainsArchitecture` | DEV output contains "Architecture" |
| `OrchestratorAgentTests` | `RunAsync_TEST_OutputContainsTestPlan` | TEST output contains "Test Plan" |
| `OrchestratorAgentTests` | `RunAsync_EmptyBrief_ThrowsArgumentException` | Empty brief throws `ArgumentException` |
| `OrchestratorAgentTests` | `RunAsync_AllAgentsExposeRoutingMetadata` | Every output includes routing metadata defaults |
| `OrchestratorAgentTests` | `RunAsync_PlanningIntent_SkipsDevAndTest` | Planning intent route excludes DEV and TEST |
| `OrchestratorAgentTests` | `RunAsync_HiringWorkflow_RunsPmHrBaByDefault` | Legacy orchestrator hiring route executes PM → HR → BA |
| `OrchestratorAgentTests` | `RunAsync_HiringWorkflow_WithTechnicalInterviewRoles_RunsPmHrBaDevTest` | Hiring workflow appends requested technical interview roles |
| `OrchestratorAgentTests` | `RunAsync_HiringWorkflow_SeedsJobDescriptionAndCvIntoAgentContext` | Hiring workflow injects JD and CV text into agent context |
| `OrchestratorAgentTests` | `RunAsync_HiringWorkflow_DEVInterviewPackContainsTechnicalInterviewQuestions` | DEV output contains hiring-specific technical interview sections |
| `HiringWorkflowTests` | `StartAsync_FitAboveThreshold_WaitsForHrApproval` | HR screening gate waits for user approval when fit is above threshold |
| `HiringWorkflowTests` | `ApproveScreening_AutoApproveDisabled_WaitsForPmApproval` | PM schedule step can wait for user approval |
| `HiringWorkflowTests` | `HiringInterview_ProgressesThroughPanelAndWritesNotes` | Interview runs by turn and writes notes to a document file |
| `HiringWorkflowTests` | `CandidateResponses_LowScore_StopsInterviewEarly` | Low interview score stops the session early |
| `HiringWorkflowTests` | `StartAsync_FitBelowThreshold_RejectsImmediately` | HR rejects low-fit CVs without forwarding them |
| `HiringWorkflowTests` | `StartAsync_CreatesPerCandidateFolderWithKeywordFiles` | Session start creates per-candidate folder with JD/CV keyword files |
| `HiringWorkflowTests` | `RequestHint_WhileInterviewActive_ReturnsHintAndSameQuestion` | Hint request returns keywords and keeps the current question active |
| `HiringWorkflowTests` | `CandidateClarificationQuestion_ReceivesInterviewerReply_SameQuestion` | Clarification message triggers an interviewer reply without advancing the question |
| `HiringWorkflowTests` | `HiringInterview_WritesLiveQaFileWithQuestionsAndAnswers` | Q&A file is appended in real-time during the interview |
| `HiringWorkflowTests` | `StartAsync_ExplicitSeniority_IsReturnedInSession` | Explicit hiring seniority is preserved in the session result |
| `InterviewSystemTests` | `BuildQuestions_UsesLlmForFullInterviewPack` | Question provider uses the LLM to generate the full staged interview pack, including technical, BA, and HR questions |
| `InterviewSystemTests` | `BuildQuestionFromNotes_UsesLiveMarkdownNotes` | Runtime question generation reads the live markdown notes file rather than requiring raw JD/CV on each interviewer turn |
| `InterviewSystemTests` | `BuildQuestions_AutoDetectsSeniorLevelFromContext` | Seniority can be inferred from hiring context when not set explicitly |
| `InterviewSystemTests` | `RuleBasedScoring_FallsBackConservativelyWithoutTranscriptEvaluation` | Conservative fallback scoring no longer evaluates transcript text and instead returns a safe non-evaluating result |
| `InterviewSystemTests` | `LlmHiringFitScoring_UsesSemanticFitAssessment` | HR screening uses semantic LLM fit scoring and returns strengths/gaps for the candidate |
| `InterviewSystemTests` | `LlmInterviewScoring_ParsesDimensionBreakdown` | LLM interview scoring parses the per-dimension breakdown and overall score correctly |
| `HarnessTests` | `RunAllAsync_HappyPath_AllScenariosProduceReport` | All 7 built-in scenarios execute and produce a report |
| `HarnessTests` | `RunAllAsync_AllRolesOutputRealContent_PassRate100Percent` | Pass rate is 100% when all role outputs are non-empty |
| `HarnessTests` | `RunScenarioAsync_DeliveryHappy_ReturnsPassedResult` | `delivery-happy` scenario passes all assertions |
| `HarnessTests` | `RunScenarioAsync_FaultEmptyLlm_OutputNotEmptyAssertionFails` | Empty LLM output causes the `output non-empty` assertion to fail |
| `HarnessTests` | `RunScenarioAsync_FaultLlmException_ScenarioPasses` | An LLM exception scenario is caught and marked as passed |
| `HarnessTests` | `RunScenarioAsync_UnknownId_Throws` | Unknown scenario ID throws `ArgumentException` |
| `HarnessTests` | `HarnessAssertionEngine_MissingSection_ReportsFailure` | Missing expected heading is captured as a `Fail` assertion |
| `HarnessTests` | `HarnessAssertionEngine_InvalidDecision_ReportsFailure` | Invalid decision value is captured as a `Fail` assertion |
| `HarnessTests` | `HarnessAssertionEngine_ConfidenceOutOfRange_ReportsFailure` | Out-of-range confidence is captured as a `Fail` assertion |
| `HarnessTests` | `RunAllAsync_ReportContainsCorrelationIdsForAllScenarios` | Every scenario result in the report has a non-empty correlation ID |
| `HarnessTests` | `DefaultScenarioProvider_HasAtLeast7Scenarios` | Built-in provider exposes at least 7 scenarios |
| `HiringHarnessTests` | `FullPanelFlow_TransitionsStagesAndWritesArtifacts` | Hiring workflow moves through approval and interview stages, then writes notes and candidate-folder artifacts |
| `HiringHarnessTests` | `FollowUpFlow_ExposesPendingFollowUpBeforeAdvancing` | The workflow exposes a pending follow-up before the interviewer advances to the next role |
| `HiringHarnessTests` | `HintFlow_LeavesQuestionActive_AndWritesHintToQaLog` | Hint requests keep the same question active and append hint output to `interview-qa.md` |
| `HiringHarnessTests` | `ClarificationFlow_RepliesWithoutAdvancingQuestion` | Clarification replies keep the same interviewer/question active |
| `HiringHarnessTests` | `EarlyStopFlow_StopsInterviewAndWritesNotes` | Low-scoring answers trigger early-stop and still persist closing notes |
| `HarnessLlmTests` | `RunAllAsync_WithRealLlm_PassRateIsAtLeast95Percent` | Full run with real LLM achieves ≥ 95% pass rate (skipped without key) |
| `HarnessLlmTests` | `RunScenarioAsync_DeliveryHappy_WithRealLlm_Passes` | `delivery-happy` passes with a real LLM response (skipped without key) |
| `HarnessLlmTests` | `RunScenarioAsync_HiringHappy_WithRealLlm_Passes` | `hiring-happy` passes with a real LLM response (skipped without key) |
| `HarnessLlmTests` | `RunAllAsync_WithRealLlm_ReportWrittenToDisk` | JSON and Markdown report files are created on disk after a real run (skipped without key) |
| `RoutingPolicyTests` | `BuildInitialRoute_PlanningIntent_ReturnsPoPmBa` | Planning intent uses PO → PM → BA route |
| `RoutingPolicyTests` | `BuildInitialRoute_BuildIntent_ReturnsPoBaDevTest` | Build intent uses PO → BA → DEV → TEST route |
| `RoutingPolicyTests` | `BuildInitialRoute_HiringWorkflow_ReturnsPmHrBa` | Hiring workflow uses PM → HR → BA route |
| `RoutingPolicyTests` | `BuildInitialRoute_HiringWorkflowWithTechnicalRoles_ReturnsPmHrBaDevTest` | Hiring workflow uses PM → HR → BA plus requested technical roles |
| `RoutingPolicyTests` | `BuildInitialRoute_HighComplexity_ReturnsFullChain` | High-complexity briefs use full-chain route |
| `RoutingPolicyTests` | `ShouldEarlyStop_StopDecisionWithHighConfidence_ReturnsTrue` | High-confidence stop decision triggers early-stop |
| `RoutingPolicyTests` | `ShouldFallbackToFullChain_EscalateDecision_ReturnsTrue` | Escalate decision triggers full-chain fallback |
