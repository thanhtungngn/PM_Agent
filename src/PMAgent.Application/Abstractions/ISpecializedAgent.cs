using PMAgent.Application.Models;

namespace PMAgent.Application.Abstractions;

/// <summary>
/// A specialized agent that fulfils a single role in the delivery team
/// (e.g. PO, PM, BA, DEV, TEST).
/// </summary>
public interface ISpecializedAgent
{
    /// <summary>Role token, e.g. "PO", "PM", "BA", "DEV", "TEST".</summary>
    string Role { get; }

    string Description { get; }

    Task<AgentTaskResult> ExecuteAsync(AgentTask task, CancellationToken cancellationToken = default);
}
