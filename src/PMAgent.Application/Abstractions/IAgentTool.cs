namespace PMAgent.Application.Abstractions;

/// <summary>
/// A single tool the agent can invoke during the action step.
/// </summary>
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default);
}
