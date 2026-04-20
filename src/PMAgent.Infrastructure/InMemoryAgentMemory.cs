using PMAgent.Application.Abstractions;

namespace PMAgent.Infrastructure;

/// <summary>
/// In-memory implementation of <see cref="IAgentMemory"/>.
/// Stores entries in an append-only list and builds context by joining them
/// as <c>[ROLE]:\ncontent</c> blocks.
/// </summary>
public sealed class InMemoryAgentMemory : IAgentMemory
{
    private readonly List<MemoryEntry> _entries = [];

    public IReadOnlyList<MemoryEntry> Entries => _entries;

    public void Record(string role, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        _entries.Add(new MemoryEntry(role.ToUpperInvariant(), content, DateTimeOffset.UtcNow));
    }

    public string BuildContext() =>
        _entries.Count == 0
            ? string.Empty
            : string.Join("\n\n", _entries.Select(e => $"[{e.Role}]:\n{e.Content}"));
}
