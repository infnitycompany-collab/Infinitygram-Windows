namespace Infinitygram.Domain;

/// <summary>
/// Multi-account coordination, one-by-one at the same pace:
/// account A adds one, then B adds one, then C, then back to A.
/// If an account gets stuck / cooled / flooded mid-job, it is skipped
/// and the job continues with the next account to the very end —
/// a stuck account never blocks the job and never loses the queue.
/// </summary>
public sealed class RoundRobinPool
{
    private readonly List<string> _order;
    private int _pointer;
    private readonly HashSet<string> _stuck = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exhausted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _cooldownUntil = new(StringComparer.Ordinal);

    public RoundRobinPool(IEnumerable<string> accountIds)
    {
        _order = accountIds.Distinct(StringComparer.Ordinal).ToList();
        if (_order.Count == 0) throw new ArgumentException("Pool needs at least one account.", nameof(accountIds));
    }

    public IReadOnlyList<string> Order => _order;
    public int AvailableCount(DateTimeOffset now) => _order.Count(a => IsAvailable(a, now));

    public bool IsAvailable(string accountId, DateTimeOffset now)
        => _order.Contains(accountId, StringComparer.Ordinal)
        && !_stuck.Contains(accountId)
        && !_exhausted.Contains(accountId)
        && (!_cooldownUntil.TryGetValue(accountId, out var until) || now >= until);

    /// <summary>Next account in order, skipping stuck/cooled/exhausted ones.
    /// Returns null only when NO account is available right now.</summary>
    public string? TryNext(DateTimeOffset now)
    {
        for (var i = 0; i < _order.Count; i++)
        {
            var idx = (_pointer + i) % _order.Count;
            var candidate = _order[idx];
            if (!IsAvailable(candidate, now)) continue;
            _pointer = (idx + 1) % _order.Count; // advance: next turn goes to the following account
            return candidate;
        }
        return null;
    }

    public void MarkStuck(string accountId) => _stuck.Add(accountId);
    public void MarkExhausted(string accountId) => _exhausted.Add(accountId);
    public void MarkCooldown(string accountId, DateTimeOffset until) => _cooldownUntil[accountId] = until;
    public void ClearStuck(string accountId) => _stuck.Remove(accountId);
    public IReadOnlyCollection<string> StuckAccounts => _stuck;
}
