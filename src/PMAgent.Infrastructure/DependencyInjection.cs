using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Abstractions.Harness;
using PMAgent.Application.Models;
using PMAgent.Infrastructure.Agents;
using PMAgent.Infrastructure.Harness;
using PMAgent.Infrastructure.Services;
using PMAgent.Infrastructure.Tools;

namespace PMAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks();

        // LLM settings + client — provider is selected by LlmSettings:Provider
        var llmSettings = configuration.GetSection("LlmSettings").Get<LlmSettings>() ?? new LlmSettings();
        var hiringWorkflowSettings = configuration.GetSection("HiringWorkflow").Get<HiringWorkflowSettings>() ?? HiringWorkflowSettings.CreateDefault();
        services.AddSingleton(llmSettings);
        services.AddSingleton(hiringWorkflowSettings);

        if (llmSettings.Provider == LlmProvider.Ollama)
        {
            services.AddScoped<ILlmClient, OllamaLlmClient>();
            healthChecks.AddCheck<OllamaHealthCheck>("ollama", tags: ["ready"]);
        }
        else
            services.AddScoped<ILlmClient, OpenAiLlmClient>();

        // LLM-backed planner — produces context-aware plans via the language model
        services.AddScoped<IAgentPlanner, LlmAgentPlanner>();
        services.AddScoped<IAgentRoutingPolicy, RuleBasedAgentRoutingPolicy>();
        services.AddScoped<IHiringFitScoringAgent, LlmHiringFitScoringAgent>();
        services.AddScoped<IInterviewQuestionProvider, ConfigurableInterviewQuestionProvider>();
        services.AddScoped<IInterviewScoringAgent, LlmInterviewScoringAgent>();
        services.AddScoped<IHiringWorkflowService, InMemoryHiringWorkflowService>();

        // Agent memory — transient so each agent instance gets its own fresh memory.
        services.AddTransient<IAgentMemory, InMemoryAgentMemory>();

        // Agent tools — registered as IAgentTool so they are all resolved
        // by IEnumerable<IAgentTool> inside AgentExecutor.
        // Each tool now delegates to ILlmClient for LLM-backed analysis.
        services.AddScoped<IAgentTool, ScopeAnalysisTool>();
        services.AddScoped<IAgentTool, RiskAssessmentTool>();
        services.AddScoped<IAgentTool, ActionPlannerTool>();

        // Agent executor loop
        services.AddScoped<IAgentExecutor, AgentExecutor>();

        // Specialized agents — registered as ISpecializedAgent so they are all
        // resolved by IEnumerable<ISpecializedAgent> inside OrchestratorAgent.
        services.AddScoped<ISpecializedAgent, ProductOwnerAgent>();
        services.AddScoped<ISpecializedAgent, ProjectManagerAgent>();
        services.AddScoped<ISpecializedAgent, HrAgent>();
        services.AddScoped<ISpecializedAgent, BusinessAnalystAgent>();
        services.AddScoped<ISpecializedAgent, DeveloperAgent>();
        services.AddScoped<ISpecializedAgent, TesterAgent>();
        services.AddScoped<ISpecializedAgent, HiringOrchestrationAgent>();

        // Orchestrator
        services.AddScoped<IOrchestratorAgent, OrchestratorAgent>();

        // Harness layer
        services.AddSingleton<IHarnessScenarioProvider, DefaultHarnessScenarioProvider>();
        services.AddScoped<IHarnessAssertionEngine, HarnessAssertionEngine>();
        services.AddScoped<IHarnessReportSink, JsonHarnessReportSink>();
        services.AddScoped<IHarnessReportSink, MarkdownHarnessReportSink>();
        services.AddScoped<IHarnessRunner, HarnessRunner>();

        return services;
    }
}
