namespace Infinitygram.Application;

/// <summary>One audited operation. Every mutation and every crypto operation
/// in the app must produce one of these — no silent operations.
/// Secrets, OTPs, tokens and passwords are NEVER stored in an audit entry.</summary>
public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string AccountId,
    string Operation,
    string Target,
    string Outcome,
    string? Detail = null);

public interface IAuditStore
{
    void Append(AuditEntry entry);
    IReadOnlyList<AuditEntry> All();
}
