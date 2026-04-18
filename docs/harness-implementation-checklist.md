# Harness Layer Implementation Checklist

> Goal: Build a Harness layer to test and operate the multi-agent flow (PO -> PM -> HR -> BA -> DEV -> TEST) in a repeatable, observable, and safe way.

## 1. Scope and Objectives

- [ ] Clearly define what the Harness is for:
- [ ] Integration harness (run end-to-end orchestrator flow).
- [ ] Evaluation harness (measure output quality against criteria).
- [ ] Reliability harness (test timeout, retry, fallback).
- [ ] Performance harness (latency, throughput, token cost).
- [ ] Define Definition of Done for each harness type.
- [ ] Define output KPIs:
- [ ] Test pass rate.
- [ ] End-to-end P95 latency.
- [ ] Output compliance rate for required markdown structure.
- [ ] Routing policy correctness rate (continue/stop/escalate).

## 2. Harness Architecture Design

- [ ] Decide where Harness implementation should live:
- [ ] Contracts/interfaces in PMAgent.Application.
- [ ] Implementations in PMAgent.Infrastructure.
- [ ] Test/runner entry points in PMAgent.Tests or a separate benchmark project.
- [ ] Define minimum abstractions for harness:
- [ ] IHarnessScenarioProvider (load scenarios).
- [ ] IHarnessRunner (execute scenarios).
- [ ] IHarnessAssertionEngine (validate outputs).
- [ ] IHarnessReportSink (write reports).
- [ ] Decide report output formats:
- [ ] JSON machine-readable report.
- [ ] Markdown human-readable summary.

## 3. Scenarios and Test Data

- [ ] Create a structured scenario set:
- [ ] Happy path (clear brief, sufficient context).
- [ ] Ambiguous path (vague brief, limited context).
- [ ] Edge path (long context, conflicting requirements).
- [ ] Failure path (LLM timeout, malformed output, missing tool).
- [ ] Standardize scenario input schema:
- [ ] ScenarioId.
- [ ] ProjectBrief.
- [ ] InitialContext.
- [ ] ExpectedSections per role.
- [ ] MaxIterationsPerAgent.
- [ ] RetryPolicy override (if needed).
- [ ] Version the scenario dataset for deterministic replay.

## 4. Output Assertions and Quality Gates

- [ ] Define required sections per role:
- [ ] PO: Vision, Goals, User Stories, Acceptance Criteria.
- [ ] PM: Milestones, Resource Plan, Risk Register.
- [ ] HR: Hiring Plan, Candidate Profile, Interview Process, Onboarding Plan.
- [ ] BA: Functional Requirements, Use Cases.
- [ ] DEV: Technology Stack, Architecture, API Design.
- [ ] TEST: Test Plan, Quality Gates.
- [ ] Add markdown format assertions:
- [ ] Required headings exist.
- [ ] Tables exist for roles that require tabular output.
- [ ] Numbered steps exist in Implementation Approach.
- [ ] Add routing metadata assertions:
- [ ] Decision is one of continue|stop|escalate.
- [ ] Confidence is within [0.0..1.0].
- [ ] On failure, report clear reasons by scenario and role.

## 5. Reliability and Error Handling

- [ ] Simulate system failure modes:
- [ ] LLM rate limit.
- [ ] Network timeout.
- [ ] Empty content response.
- [ ] Partial agent failure.
- [ ] Define retry policy for harness runs:
- [ ] Retry count.
- [ ] Backoff strategy.
- [ ] Maximum execution budget.
- [ ] Ensure cancellation token propagation end to end.
- [ ] Verify orchestrator fallback behavior when one role fails.

## 6. Observability in Harness

- [ ] Enable structured logging in Harness runner.
- [ ] Add CorrelationId per harness run.
- [ ] Log per-role start/end and elapsed milliseconds.
- [ ] Log scenario metadata (never log secrets).
- [ ] Collect metrics:
- [ ] Total duration and per-role duration.
- [ ] Token usage (if provider supports it).
- [ ] Success/failure rate per role.
- [ ] Standardize report outputs:
- [ ] Top-level summary.
- [ ] Detailed step trace.
- [ ] Failed assertion list.

## 7. Security and Secret Hygiene

- [ ] Never hardcode API keys in test data or committed config.
- [ ] Confirm appsettings.Development.json is never tracked by git.
- [ ] Use environment variables or secret store for LLM settings.
- [ ] Redact prompts/responses if sensitive data appears.
- [ ] Add secret scanning checks in CI before push.

## 8. CI/CD Integration

- [ ] Add test categories/tags for harness tests (for example: Harness, E2E, LLM).
- [ ] Split into 2 run modes:
- [ ] Fast mode with fake LLM for PR gating.
- [ ] Full mode with real LLM for nightly validation.
- [ ] Upload report artifacts (JSON + Markdown) after each run.
- [ ] Set pipeline fail thresholds (for example pass rate < 95%).
- [ ] If benchmark mode exists, track latency trend over time.

## 9. Sprint-Based Implementation Plan

- [ ] Sprint 1:
- [ ] Create IHarness* contracts in Application.
- [ ] Implement a basic runner in Infrastructure.
- [ ] Add 3 happy-path scenarios with heading assertions.
- [ ] Sprint 2:
- [ ] Add failure scenarios with retry/backoff behavior.
- [ ] Add JSON/Markdown report sinks.
- [ ] Integrate logging and correlation.
- [ ] Sprint 3:
- [ ] Integrate CI modes (fast/nightly).
- [ ] Add quality gates and thresholds.
- [ ] Review and tune prompts using harness reports.

## 10. Harness Layer Definition of Done

- [ ] Runnable locally with a single command.
- [ ] At least 10 replayable scenarios.
- [ ] JSON and Markdown reports produced for every run.
- [ ] Assertions cover all 6 roles.
- [ ] Timeout/retry/fallback tests implemented.
- [ ] CI includes both fast and nightly full jobs.
- [ ] Technical/business docs updated when interfaces or behavior change.

## 11. Suggested Run Commands (Draft)

- [ ] dotnet test tests/PMAgent.Tests/PMAgent.Tests.csproj --filter "Category=Harness"
- [ ] dotnet test tests/PMAgent.Tests/PMAgent.Tests.csproj --filter "Category=HarnessLLM"
- [ ] dotnet test --logger "trx;LogFileName=harness.trx"

## 12. Risks to Monitor

- [ ] Prompt drift can make output unstable over time.
- [ ] Real LLM testing cost can increase quickly as scenarios grow.
- [ ] False negatives caused by overly strict assertions.
- [ ] False positives caused by overly loose assertions.
- [ ] CI build time can increase significantly with large full-suite runs.

## 13. Decision Log (to fill during implementation)

- [ ] Chosen markdown assertion framework:
- [ ] Chosen standard report format:
- [ ] Chosen pass-rate threshold:
- [ ] Chosen default retry policy:
- [ ] Chosen nightly schedule:
