using System.Collections.Concurrent;
using Infinitygram.Application;

namespace Infinitygram.Infrastructure;

/// <summary>P0 in-memory audit store. P6 replaces it with the durable SQLite audit
/// table with the same interface — no caller changes.</summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();
    public void Append(AuditEntry entry) => _entries.Enqueue(entry);
    public IReadOnlyList<AuditEntry> All() => _entries.ToArray();
}
