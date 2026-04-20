namespace PMAgent.Application.Abstractions;

/// <summary>
/// Maintains a structured log of all agent interactions for a single run,
/// enabling subsequent reasoning steps to reference prior outputs without
/// relying on manual string concatenation.
/// </summary>
public interface IAgentMemory
{
    /// <summary>Records an entry produced by a named role or component.</summary>
    void Record(string role, string content);

    /// <summary>
    /// Builds a flattened context string from all recorded entries,
    /// formatted as <c>[ROLE]:\ncontent</c> blocks separated by blank lines.
    /// </summary>
    string BuildContext();

    /// <summary>All entries in insertion order.</summary>
    IReadOnlyList<MemoryEntry> Entries { get; }
}

/// <summary>A single recorded entry in agent memory.</summary>
public sealed record MemoryEntry(string Role, string Content, DateTimeOffset RecordedAt);
