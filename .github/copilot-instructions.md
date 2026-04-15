# GitHub Copilot Instructions — PM Agent

These rules apply to every AI-assisted code change in this repository.

---

## Documentation Update Rule

> **MANDATORY: Any code change made by Copilot or any AI tool MUST be accompanied
> by the corresponding documentation update in the same commit/PR.**

### What to update and when

| Code change | Update required |
|---|---|
| New `IAgentTool` implementation added | Add a row to the **Built-in Tools** table in `docs/technical.md`. If the tool has user-visible behaviour, describe it in the **Key Capabilities** section of `docs/business.md`. |
| New `ISpecializedAgent` implementation added | Extend `SpecializedAgentBase`; add a row to the **Specialized Agents** table in `docs/technical.md`; describe the role in the **Key Capabilities** section of `docs/business.md`. |
| New API endpoint added or modified | Update the **API Reference** section in `docs/technical.md`. Update the **How to Request a Plan** section in `docs/business.md` if the change affects the user-facing interface. |
| `AgentExecutor` loop logic changed | Update the **Agent Loop** and **`AgentExecutor` — Internal Flow** sections in `docs/technical.md`. Update the **How the Agent Reasons** table in `docs/business.md` if the reasoning sequence changes. |
| Data model changed (`AgentStep`, `AgentRunRequest`, `AgentRunResult`) | Update the **Data Models** section in `docs/technical.md`. Update the **What the Response Looks Like** table in `docs/business.md` if the change is user-visible. |
| New interface or abstraction added | Update the **Interfaces** section in `docs/technical.md`. |
| DI registration changed | Update the **Dependency Injection** section in `docs/technical.md`. |
| New test added or test behaviour changed | Update the **Test Coverage** table in `docs/technical.md`. |
| Solution structure changed (new project, folder, etc.) | Update the **Solution Structure** diagram in `docs/technical.md`. |
| Glossary term added or renamed | Update the **Glossary** in `docs/business.md`. |

---

## Document Locations

| Document | Path | Audience |
|---|---|---|
| Technical documentation | `docs/technical.md` | Developers & architects |
| Business documentation | `docs/business.md` | Product owners, BAs, project managers |

---

## Code Style Rules

- Follow Clean Architecture layering: contracts go in `PMAgent.Application`; implementations go in `PMAgent.Infrastructure`; HTTP controllers go in `PMAgent.Api`.
- New tools must implement `IAgentTool` and be registered via `AddScoped<IAgentTool, YourTool>()` in `DependencyInjection.cs`.
- All new public types in `PMAgent.Application` must be `sealed record` unless a class hierarchy is explicitly required.
- Every new feature must have at least one xUnit test in `PMAgent.Tests`.
- Do not add implementation logic to `PMAgent.Application` — it must remain a pure contracts/models layer.
