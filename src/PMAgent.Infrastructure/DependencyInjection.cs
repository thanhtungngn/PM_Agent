using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using PMAgent.Infrastructure.Agents;
using PMAgent.Infrastructure.Services;
using PMAgent.Infrastructure.Tools;

namespace PMAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // LLM settings + client — provider is selected by LlmSettings:Provider
        var llmSettings = configuration.GetSection("LlmSettings").Get<LlmSettings>() ?? new LlmSettings();
        services.AddSingleton(llmSettings);

        if (llmSettings.Provider == LlmProvider.Ollama)
        {
            services.AddScoped<ILlmClient, OllamaLlmClient>();
            services.AddHealthChecks()
                    .AddCheck<OllamaHealthCheck>("ollama", tags: ["ready"]);
        }
        else
            services.AddScoped<ILlmClient, OpenAiLlmClient>();

        // Simple rule-based planner (legacy endpoint)
        services.AddScoped<IAgentPlanner, RuleBasedAgentPlanner>();

        // Agent tools — registered as IAgentTool so they are all resolved
        // by IEnumerable<IAgentTool> inside AgentExecutor.
        services.AddScoped<IAgentTool, ScopeAnalysisTool>();
        services.AddScoped<IAgentTool, RiskAssessmentTool>();
        services.AddScoped<IAgentTool, ActionPlannerTool>();

        // Agent executor loop
        services.AddScoped<IAgentExecutor, AgentExecutor>();

        // Specialized agents — registered as ISpecializedAgent so they are all
        // resolved by IEnumerable<ISpecializedAgent> inside OrchestratorAgent.
        services.AddScoped<ISpecializedAgent, ProductOwnerAgent>();
        services.AddScoped<ISpecializedAgent, ProjectManagerAgent>();
        services.AddScoped<ISpecializedAgent, BusinessAnalystAgent>();
        services.AddScoped<ISpecializedAgent, DeveloperAgent>();
        services.AddScoped<ISpecializedAgent, TesterAgent>();

        // Orchestrator
        services.AddScoped<IOrchestratorAgent, OrchestratorAgent>();

        return services;
    }
}
