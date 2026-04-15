using Microsoft.Extensions.Logging;
using PMAgent.Application.Abstractions;
using PMAgent.Application.Models;
using System.Diagnostics;

namespace PMAgent.Infrastructure.Agents;

/// <summary>
/// Base class for all LLM-backed specialized agents.
/// Subclasses only need to provide Role, Description, and SystemPrompt.
/// </summary>
public abstract class SpecializedAgentBase : ISpecializedAgent
{
    private readonly ILlmClient _llm;
    private readonly ILogger _logger;

    protected SpecializedAgentBase(ILlmClient llm, ILogger logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public abstract string Role { get; }
    public abstract string Description { get; }
    protected abstract string SystemPrompt { get; }

    public async Task<AgentTaskResult> ExecuteAsync(
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[{Role}] Starting. Goal: {Goal}", Role, task.Goal);
        var sw = Stopwatch.StartNew();

        var userMessage = BuildUserMessage(task);
        var output = await _llm.CompleteAsync(SystemPrompt, userMessage, cancellationToken);

        sw.Stop();
        _logger.LogInformation("[{Role}] Completed in {ElapsedMs} ms. Output length: {Chars} chars.",
            Role, sw.ElapsedMilliseconds, output.Length);
        _logger.LogDebug("[{Role}] Output:\n{Output}", Role, output);

        return new AgentTaskResult(Role, output, true);
    }

    private static string BuildUserMessage(AgentTask task) =>
        string.IsNullOrWhiteSpace(task.Context)
            ? $"Project Brief: {task.Goal}"
            : $"Project Brief: {task.Goal}\n\nContext from previous team members:\n{task.Context}";
}
