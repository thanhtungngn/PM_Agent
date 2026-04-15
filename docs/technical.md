# PM Agent — Technical Documentation

> **Audience:** Developers and architects.
> For business context, see [business.md](business.md).

---

## Solution Structure

```
PMAgent.slnx
├── src/
│   ├── PMAgent.Api              → HTTP controllers, request validation
│   ├── PMAgent.Application      → Interfaces + data models (no implementation)
│   ├── PMAgent.Domain           → Domain entities (extensible placeholder)
│   └── PMAgent.Infrastructure   → AgentExecutor, OrchestratorAgent,
│                                  Tools/, Agents/, DI registration
└── tests/
    └── PMAgent.Tests            → xUnit unit tests
```

### Layer responsibilities

| Layer | Responsibility |
|---|---|
| **Api** | Expose HTTP endpoints, validate request surface, delegate to application/infrastructure |
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
│  1. THINK      Decide next action based on goal +       │
│                accumulated context + completed steps    │
│                                                         │
│  2. ACTION     Select a tool by name                    │
│                                                         │
│  3. INPUT      Pass a string input to the tool          │
│                                                         │
│  4. OUTPUT     Receive the tool's string result         │
│                                                         │
│  5. IsFinal?   true  → append step, break loop         │
│                false → append output to context, +1     │
└─────────────────────────────────────────────────────────┘
```

**Iteration trace (default tool sequence):**

```
Iteration 0: Think → scope_analysis    (IsFinal: false)
Iteration 1: Think → risk_assessment   (IsFinal: false)
Iteration 2: Think → action_planner    (IsFinal: false)
Iteration 3: Think → finalize          (IsFinal: true)  ← loop exits
```

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
    string Role,     // Target agent role, e.g. "PO", "PM", "BA", "DEV", "TEST"
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
    bool   Success  // Whether the agent completed its task successfully
);
```

### `OrchestrationRequest`

```csharp
// src/PMAgent.Application/Models/OrchestrationRequest.cs
record OrchestrationRequest(
    string ProjectBrief,              // High-level description of the project
    string Context = "",              // Optional background context
    int    MaxIterationsPerAgent = 10 // Loop safety cap per agent; valid range: 1–50
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

---

## Built-in Tools

| Tool name | Class | File |
|---|---|---|
| `scope_analysis` | `ScopeAnalysisTool` | `src/PMAgent.Infrastructure/Tools/ScopeAnalysisTool.cs` |
| `risk_assessment` | `RiskAssessmentTool` | `src/PMAgent.Infrastructure/Tools/RiskAssessmentTool.cs` |
| `action_planner` | `ActionPlannerTool` | `src/PMAgent.Infrastructure/Tools/ActionPlannerTool.cs` |

### How to add a new tool

1. Create a class implementing `IAgentTool` in `src/PMAgent.Infrastructure/Tools/`.
2. Register it in `DependencyInjection.cs`:
   ```csharp
   services.AddScoped<IAgentTool, YourNewTool>();
   ```
3. Update the `Think` method inside `AgentExecutor` to invoke the new tool at the right point in the reasoning sequence.
4. **Update `docs/technical.md`** — add a row to the Built-in Tools table.
5. **Update `docs/business.md`** — if the tool adds user-visible behaviour, describe it in the Capabilities section.

---

## Specialized Agents

The orchestrator pattern adds five role-specific agents. All five extend `SpecializedAgentBase`, which centralises the shared logic:
- Constructor injection of `ILlmClient`
- `BuildUserMessage` — formats the goal + accumulated context into a single user prompt
- `ExecuteAsync` — calls `ILlmClient.CompleteAsync` and wraps the result in `AgentTaskResult`

Each concrete agent only declares three things:

```csharp
// src/PMAgent.Infrastructure/Agents/SpecializedAgentBase.cs
public abstract class SpecializedAgentBase : ISpecializedAgent
{
    protected SpecializedAgentBase(ILlmClient llm) => _llm = llm;

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
| `BA` | `BusinessAnalystAgent` | Functional requirements, use cases, gap analysis |
| `DEV` | `DeveloperAgent` | Tech stack, architecture, API design, implementation approach |
| `TEST` | `TesterAgent` | Test plan, test types, quality gates, coverage targets |

All agents live in `src/PMAgent.Infrastructure/Agents/`.

### Orchestrator dispatch sequence

```
OrchestratorAgent.RunAsync(OrchestrationRequest)
│
├── AgentTask("PO",   brief, context)    → ProductOwnerAgent
│   └── appends PO output to context
├── AgentTask("PM",   brief, context+PO) → ProjectManagerAgent
│   └── appends PM output to context
├── AgentTask("BA",   brief, context+PM) → BusinessAnalystAgent
│   └── appends BA output to context
├── AgentTask("DEV",  brief, context+BA) → DeveloperAgent
│   └── appends DEV output to context
└── AgentTask("TEST", brief, context+DEV)→ TesterAgent
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
3. Add the role token to the `AgentOrder` array in `OrchestratorAgent` at the correct position.
4. **Update `docs/technical.md`** — add a row to the Specialized Agents table.
5. **Update `docs/business.md`** — describe the new role in the Key Capabilities section.

---

## `AgentExecutor` — Internal Flow

```
AgentExecutor(IEnumerable<IAgentTool> tools)
│  Builds: Dictionary<string, IAgentTool>  (case-insensitive by tool Name)
│
└── RunAsync(AgentRunRequest request)
      │
      ├── runningContext = request.Context
      │
      └── for iteration in [0, MaxIterations)
            │
            ├── Think(goal, runningContext, completedSteps)
            │     Inspects completed step actions to decide next tool
            │     Returns: (thought, action, actionInput, isFinal)
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
            ├── runningContext += "\n[ACTION]: actionOutput"
            │
            └── if isFinal → break
      │
      └── return AgentRunResult(finalAnswer, steps)
```

---

## API Reference

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
  "maxIterationsPerAgent": 10
}
```

| Field | Type | Required | Constraints |
|---|---|---|---|
| `projectBrief` | string | yes | Non-empty |
| `context` | string | no | Free text; seeds the reasoning context |
| `maxIterationsPerAgent` | int | no | 1–50; default: 10 |

**Response — `200 OK`**

```json
{
  "summary": "# Project Orchestration Summary\n\n**Brief:** Build a SaaS...\n...",
  "agentOutputs": [
    { "role": "PO",   "output": "## Product Owner Output...", "success": true },
    { "role": "PM",   "output": "## Project Manager Output...", "success": true },
    { "role": "BA",   "output": "## Business Analyst Output...", "success": true },
    { "role": "DEV",  "output": "## Developer Output...", "success": true },
    { "role": "TEST", "output": "## Tester Output...", "success": true }
  ]
}
```

**Error responses**

| Status | Condition |
|---|---|
| `400 Bad Request` | `projectBrief` is empty or whitespace |
| `400 Bad Request` | `maxIterationsPerAgent` is outside 1–50 |

### `POST /api/planning/next-actions` _(legacy)_

Calls the rule-based `RuleBasedAgentPlanner`. Returns a flat `PlanningResponse` (summary, next actions, risks). Does not use the agent loop.

---

## Dependency Injection

```csharp
// src/PMAgent.Api/Program.cs
builder.Services.AddInfrastructure();
```

```csharp
// src/PMAgent.Infrastructure/DependencyInjection.cs
services.AddScoped<IAgentPlanner, RuleBasedAgentPlanner>(); // legacy endpoint

services.AddScoped<IAgentTool, ScopeAnalysisTool>();        // registered as IAgentTool
services.AddScoped<IAgentTool, RiskAssessmentTool>();        // all resolved together
services.AddScoped<IAgentTool, ActionPlannerTool>();         // by IEnumerable<IAgentTool>

services.AddScoped<IAgentExecutor, AgentExecutor>();

services.AddScoped<ISpecializedAgent, ProductOwnerAgent>();  // registered as ISpecializedAgent
services.AddScoped<ISpecializedAgent, ProjectManagerAgent>(); // all resolved together
services.AddScoped<ISpecializedAgent, BusinessAnalystAgent>();// by IEnumerable<ISpecializedAgent>
services.AddScoped<ISpecializedAgent, DeveloperAgent>();
services.AddScoped<ISpecializedAgent, TesterAgent>();

services.AddScoped<IOrchestratorAgent, OrchestratorAgent>();
```

`AgentExecutor` receives all `IAgentTool` registrations via constructor injection as `IEnumerable<IAgentTool>`. `OrchestratorAgent` uses the same pattern with `IEnumerable<ISpecializedAgent>`. Adding a new agent requires only one `AddScoped` line.

---

## Running the Solution

```bash
# Start the API (HTTP on port configured in launchSettings.json)
dotnet run --project src/PMAgent.Api

# Run all tests
dotnet test PMAgent.slnx
```

---

## Test Coverage

| Test class | Test | What is validated |
|---|---|---|
| `RuleBasedAgentPlannerTests` | `BuildPlanAsync_ReturnsActionsAndRisks` | Legacy planner returns actions and risks |
| `AgentExecutorTests` | `RunAsync_ProducesIsFinalStep` | At least one step has `IsFinal = true` |
| `AgentExecutorTests` | `RunAsync_LastStepIsAlwaysFinal` | The last step is always the final step |
| `AgentExecutorTests` | `RunAsync_AllThreeToolsAreInvoked` | All three built-in tools execute in a normal run |
| `AgentExecutorTests` | `RunAsync_EachStepHasNonEmptyThoughtAndOutput` | Every step has non-empty `Thought` and `ActionOutput` |
| `AgentExecutorTests` | `RunAsync_FinalAnswerContainsGoal` | `FinalAnswer` includes the original goal string |
| `AgentExecutorTests` | `RunAsync_RespectsMaxIterationsGuard` | `MaxIterations = 1` produces exactly one step |
| `OrchestratorAgentTests` | `RunAsync_RunsAllFiveAgents` | `AgentOutputs.Count == 5` |
| `OrchestratorAgentTests` | `RunAsync_OutputContainsAllRoles` | All five role tokens present in outputs |
| `OrchestratorAgentTests` | `RunAsync_AllAgentsReportSuccess` | Every `AgentTaskResult.Success == true` |
| `OrchestratorAgentTests` | `RunAsync_SummaryIsNotEmpty` | `Summary` is non-empty |
| `OrchestratorAgentTests` | `RunAsync_PO_OutputContainsBrief` | PO output includes the project brief string |
| `OrchestratorAgentTests` | `RunAsync_PM_OutputContainsMilestones` | PM output contains "Milestone" |
| `OrchestratorAgentTests` | `RunAsync_BA_OutputContainsRequirements` | BA output contains "Requirement" |
| `OrchestratorAgentTests` | `RunAsync_DEV_OutputContainsArchitecture` | DEV output contains "Architecture" |
| `OrchestratorAgentTests` | `RunAsync_TEST_OutputContainsTestPlan` | TEST output contains "Test Plan" |
| `OrchestratorAgentTests` | `RunAsync_EmptyBrief_ThrowsArgumentException` | Empty brief throws `ArgumentException` |
