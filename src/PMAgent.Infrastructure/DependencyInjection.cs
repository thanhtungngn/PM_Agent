using Microsoft.Extensions.DependencyInjection;
using PMAgent.Application.Abstractions;
using PMAgent.Infrastructure.Services;

namespace PMAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IAgentPlanner, RuleBasedAgentPlanner>();
        return services;
    }
}
