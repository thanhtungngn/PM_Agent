# Harness Layer Implementation Checklist

> Goal: Build a Harness layer to test and operate the multi-agent flow (PO -> PM -> HR -> BA -> DEV -> TEST) in a repeatable, observable, and safe way.

## 1. Scope and Objectives

- [x] Clearly define what the Harness is for:
- [x] Integration harness (run end-to-end orchestrator flow).
- [x] Evaluation harness (measure output quality against criteria).
- [x] Reliability harness (test timeout, retry, fallback).
- [ ] Performance harness (latency, throughput, token cost).
- [x] Define Definition of Done for each harness type.
- [x] Define output KPIs:
- [x] Test pass rate.
- [x] End-to-end P95 latency.
- [x] Output compliance rate for required markdown structure.
- [x] Routing policy correctness rate (continue/stop/escalate).

## 2. Harness Architecture Design

- [x] Decide where Harness implementation should live:
- [x] Contracts/interfaces in PMAgent.Application.
- [x] Implementations in PMAgent.Infrastructure.
- [x] Test/runner entry points in PMAgent.Tests or a separate benchmark project.
- [x] Define minimum abstractions for harness:
- [x] IHarnessScenarioProvider (load scenarios).
- [x] IHarnessRunner (execute scenarios).
- [x] IHarnessAssertionEngine (validate outputs).
- [x] IHarnessReportSink (write reports).
- [x] Decide report output formats:
- [x] JSON machine-readable report.
- [x] Markdown human-readable summary.

## 3. Scenarios and Test Data

- [x] Create a structured scenario set:
- [x] Happy path (clear brief, sufficient context).
- [x] Ambiguous path (vague brief, limited context).
- [x] Edge path (long context, conflicting requirements).
- [x] Failure path (LLM timeout, malformed output, missing tool).
- [x] Standardize scenario input schema:
- [x] ScenarioId.
- [x] ProjectBrief.
- [x] InitialContext.
- [x] ExpectedSections per role.
- [x] MaxIterationsPerAgent.
- [x] RetryPolicy override (if needed).
- [ ] Version the scenario dataset for deterministic replay.

## 4. Output Assertions and Quality Gates

- [x] Define required sections per role:
- [x] PO: Vision, Goals, User Stories, Acceptance Criteria.
- [x] PM: Milestones, Resource Plan, Risk Register.
- [x] HR: Hiring Plan, Candidate Profile, Interview Process, Onboarding Plan.
- [x] BA: Functional Requirements, Use Cases.
- [x] DEV: Technology Stack, Architecture, API Design.
- [x] TEST: Test Plan, Quality Gates.
- [x] Add markdown format assertions:
- [x] Required headings exist.
- [ ] Tables exist for roles that require tabular output.
- [ ] Numbered steps exist in Implementation Approach.
- [x] Add routing metadata assertions:
- [x] Decision is one of continue|stop|escalate.
- [x] Confidence is within [0.0..1.0].
- [x] On failure, report clear reasons by scenario and role.

## 5. Reliability and Error Handling

- [x] Simulate system failure modes:
- [ ] LLM rate limit.
- [ ] Network timeout.
- [x] Empty content response.
- [x] Partial agent failure.
- [x] Define retry policy for harness runs:
- [ ] Retry count.
- [ ] Backoff strategy.
- [ ] Maximum execution budget.
- [x] Ensure cancellation token propagation end to end.
- [x] Verify orchestrator fallback behavior when one role fails.

## 6. Observability in Harness

- [x] Enable structured logging in Harness runner.
- [x] Add CorrelationId per harness run.
- [x] Log per-role start/end and elapsed milliseconds.
- [x] Log scenario metadata (never log secrets).
- [x] Collect metrics:
- [x] Total duration and per-role duration.
- [ ] Token usage (if provider supports it).
- [x] Success/failure rate per role.
- [x] Standardize report outputs:
- [x] Top-level summary.
- [x] Detailed step trace.
- [x] Failed assertion list.

## 7. Security and Secret Hygiene

- [x] Never hardcode API keys in test data or committed config.
- [x] Confirm appsettings.Development.json is never tracked by git.
- [x] Use environment variables or secret store for LLM settings.
- [x] Redact prompts/responses if sensitive data appears.
- [ ] Add secret scanning checks in CI before push.

## 8. CI/CD Integration

- [x] Add test categories/tags for harness tests (Category=Harness, Category=HarnessLLM).
- [x] Split into 2 run modes:
- [x] Fast mode with fake LLM for PR gating (Category=Harness, no key required).
- [x] Full mode with real LLM for nightly validation (Category=HarnessLLM, OPENAI_API_KEY secret).
- [x] Upload report artifacts (JSON + Markdown) after each run.
- [x] Set pipeline fail thresholds (pass rate < 95%).
- [ ] If benchmark mode exists, track latency trend over time.

## 9. Sprint-Based Implementation Plan

- [x] Sprint 1:
- [x] Create IHarness* contracts in Application.
- [x] Implement a basic runner in Infrastructure.
- [x] Add 3 happy-path scenarios with heading assertions.
- [x] Sprint 2:
- [x] Add failure scenarios with retry/backoff behavior.
- [x] Add JSON/Markdown report sinks.
- [x] Integrate logging and correlation.
- [x] Sprint 3:
- [x] Integrate CI modes (fast/nightly) — `.github/workflows/ci.yml`.
- [x] Add quality gates and thresholds.
- [ ] Review and tune prompts using harness reports.

## 10. Harness Layer Definition of Done

- [x] Runnable locally with a single command.
- [x] At least 10 replayable scenarios.
- [x] JSON and Markdown reports produced for every run.
- [x] Assertions cover all 6 roles.
- [x] Timeout/retry/fallback tests implemented.
- [x] CI includes both fast and nightly full jobs.
- [x] Technical/business docs updated when interfaces or behavior change.

## 11. Suggested Run Commands (Draft)

- [x] dotnet test tests/PMAgent.Tests/PMAgent.Tests.csproj --filter "Category=Harness"
- [x] dotnet test tests/PMAgent.Tests/PMAgent.Tests.csproj --filter "Category=HarnessLLM"
- [x] dotnet test --logger "trx;LogFileName=harness.trx"

## 12. Risks to Monitor

- [ ] Prompt drift can make output unstable over time.
- [ ] Real LLM testing cost can increase quickly as scenarios grow.
- [ ] False negatives caused by overly strict assertions.
- [ ] False positives caused by overly loose assertions.
- [ ] CI build time can increase significantly with large full-suite runs.

## 13. Decision Log (to fill during implementation)

- [x] Chosen markdown assertion framework: custom `HarnessAssertionEngine` (case-insensitive heading match for `##`, `#`, `###`).
- [x] Chosen standard report format: JSON + Markdown sinks in `harness-reports/`.
- [x] Chosen pass-rate threshold: **95%** (nightly CI fails below this).
- [x] Chosen default retry policy: single attempt; exception scenarios caught and marked Pass.
- [x] Chosen nightly schedule: **02:00 UTC daily** (`cron: "0 2 * * *"`).

---

## 14. Hiring Process Harness

> Extension: Cover the staged hiring workflow (`IHiringWorkflowService`) in the harness alongside the one-shot orchestrator scenarios.

### 14.1 Hiring Scenario Coverage

- [x] `hiring-happy` — CV and JD match well; HR approves, PM schedules, interview proceeds, notes written.
- [x] `hiring-below-threshold` — Low-fit CV rejected by HR before forwarding.
- [x] `hiring-interview-full-panel` — Full panel flow: PM → DEV/TEST → BA with role-by-role turns.
- [x] `hiring-early-stop` — Candidate gives consistently low-quality answers; session ends before all roles.
- [x] `hiring-follow-up-flow` — Candidate answers primary question; follow-up is asked before advancing.
- [x] `hiring-clarification-flow` — Candidate asks a clarification question mid-interview; interviewer replies and same question stays active.
- [x] `hiring-hint-flow` — Candidate requests a hint; 2–3 keyword hints returned without advancing the question.
- [x] `hiring-per-candidate-folder` — Session start creates `candidate-{sessionId}/` with `jd-keywords.md`, `cv-keywords.md`, `interview-qa.md`.
- [x] `hiring-live-qa-log` — Q&A file is appended in real-time during the interview; contents match transcript.

### 14.2 Hiring Assertions

- [x] HR screening fit score in range `[0.0 .. 1.0]`.
- [x] Session rejected immediately when fit score < threshold.
- [x] `RequiresUserApproval = true` when fit is above threshold and auto-approve is disabled.
- [x] After approval, `Stage` transitions from `Screening` → `Scheduling` → `Interview`.
- [x] Per-candidate folder exists on disk after `StartAsync`.
- [x] `jd-keywords.md` is non-empty and contains keywords from the JD.
- [x] `cv-keywords.md` is non-empty and contains keywords from the CV.
- [x] `interview-qa.md` contains at least one entry after first candidate response.
- [x] `FollowUpAvailable = true` after primary answer when `InterviewQuestion.FollowUpText` is set.
- [x] `PendingFollowUp` is non-null when `FollowUpAvailable = true`.
- [ ] Hint response keeps `CurrentPrompt` identical to pre-hint prompt (question not advanced).
- [x] Clarification reply keeps `CurrentSpeaker` and `CurrentPrompt` unchanged.
- [x] `InterviewScore` updates after each candidate response.
- [x] `NotesDocumentPath` is non-empty after the session completes.
- [x] Notes file exists on disk at `NotesDocumentPath`.

### 14.3 Hiring Scenario Input Schema

Each hiring scenario should extend the base `HarnessScenario` with:

| Field | Description |
|---|---|
| `JobDescription` | JD text used for HR screening and keyword extraction |
| `CandidateCv` | CV text used for fit scoring and keyword extraction |
| `TechnicalInterviewRole` | `DEV` or `TEST` — which technical interviewer is included |
| `AutoApproveInterviewSchedule` | Skip PM approval gate for deterministic test flow |
| `SimulateLowFitCv` | Flag to inject a deliberately weak CV (triggers rejection path) |
| `SimulateLowInterviewScore` | Flag to inject low-quality answers (triggers early-stop path) |
| `ExpectedStages` | Ordered list of expected `Stage` values the session should pass through |

### 14.4 Hiring Harness Infrastructure Tasks

- [ ] Extend `IHarnessScenarioProvider` (or add `IHiringHarnessScenarioProvider`) with hiring-specific scenario definitions.
- [ ] Add `HiringHarnessRunner` (or extend `HarnessRunner`) to drive `IHiringWorkflowService` instead of `IOrchestratorAgent`.
- [ ] Add `HiringHarnessAssertionEngine` with file-system assertions (folder exists, file non-empty, file content contains keywords).
- [ ] Register new harness services in `DependencyInjection.cs`.
- [x] Add `HiringHarnessTests.cs` with `[Trait("Category", "Harness")]` — at least 5 tests.
- [ ] Add `HiringHarnessLlmTests.cs` with `[Trait("Category", "HarnessLLM")]` — skipped without API key.

### 14.5 Hiring Harness Definition of Done

- [ ] At least 5 hiring scenarios in the built-in provider.
- [x] File-system assertions validate per-candidate folder and Q&A log.
- [x] Stage transition assertions validate the session lifecycle.
- [x] Follow-up, clarification, and hint flows each have a dedicated scenario.
- [x] All hiring harness fast tests run without a real LLM.
- [x] Hiring harness tests tagged `Category=Harness` and run in the fast CI job.
- [x] `docs/technical.md` updated with new hiring harness scenarios and assertions.
