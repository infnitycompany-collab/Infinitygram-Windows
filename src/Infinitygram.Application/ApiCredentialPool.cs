using System.Text.RegularExpressions;

namespace Infinitygram.Application;

/// <summary>One Telegram app credential pair.</summary>
public sealed record ApiPair(long ApiId, string ApiHash);

/// <summary>
/// api_id / api_hash pool — the ban-protection rule:
/// ONE pair per account, NEVER duplicated across accounts in the same run.
/// Duplication is a hard import error, not a warning: sharing one app identity
/// across many user accounts is exactly what gets accounts linked and banned together.
/// Bulk import: a txt file, one pair per line, e.g. 500 lines:
///   123456,0123456789abcdef0123456789abcdef
/// Separators accepted: comma, semicolon, colon, tab, space. Blank lines and
/// lines starting with # are ignored. Pairs are held in memory for import only;
/// the caller must seal each assigned pair into the Vault per account immediately
/// and must never persist this pool as plain text.
/// Assignment is automatic and in order: account 1 gets pair 1, account 2 gets
/// pair 2, and so on. When the pool is exhausted it repeats the same order
/// from the start (cycle count increments) — assignment order depends on the
/// accounts' order, not on chance.
/// </summary>
public sealed class ApiCredentialPool
{
    private readonly List<ApiPair> _pairs;
    private readonly Dictionary<string, ApiPair> _assigned = new(StringComparer.Ordinal);
    private int _nextIndex;
    public int CycleCount { get; private set; }
    public int PairCount => _pairs.Count;
    public int AssignedCount => _assigned.Count;

    private ApiCredentialPool(List<ApiPair> pairs) => _pairs = pairs;

    public static (ApiCredentialPool Pool, List<string> Errors) ParseTxt(string txt)
    {
        var pairs = new List<ApiPair>();
        var errors = new List<string>();
        var seenIds = new HashSet<long>();
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = (txt ?? "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split([',', ';', ':', '\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                errors.Add($"Line {i + 1}: expected 'api_id,api_hash', got {parts.Length} fields.");
                continue;
            }
            if (!long.TryParse(parts[0], out var apiId) || apiId <= 0)
            {
                errors.Add($"Line {i + 1}: invalid api_id '{parts[0]}'.");
                continue;
            }
            var hash = parts[1].Trim().ToLowerInvariant();
            if (hash.Length != 32 || !Regex.IsMatch(hash, "^[0-9a-f]{32}$"))
            {
                errors.Add($"Line {i + 1}: api_hash must be 32 hex chars.");
                continue;
            }
            if (!seenIds.Add(apiId))
            {
                errors.Add($"Line {i + 1}: duplicate api_id {apiId} — one pair per account, duplicates rejected.");
                continue;
            }
            if (!seenHashes.Add(hash))
            {
                errors.Add($"Line {i + 1}: duplicate api_hash — one pair per account, duplicates rejected.");
                continue;
            }
            pairs.Add(new ApiPair(apiId, hash));
        }
        return (new ApiCredentialPool(pairs), errors);
    }

    /// <summary>Assign (or return the already-assigned) pair for an account, in pool order.</summary>
    public ApiPair Assign(string accountId)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountId);
        if (_assigned.TryGetValue(accountId, out var existing)) return existing;
        if (_pairs.Count == 0) throw new InvalidOperationException("ApiCredentialPool is empty: import a txt file first.");
        if (_nextIndex >= _pairs.Count)
        {
            _nextIndex = 0;
            CycleCount++;
        }
        var pair = _pairs[_nextIndex++];
        _assigned[accountId] = pair;
        return pair;
    }

    public bool TryGetAssigned(string accountId, out ApiPair pair)
        => _assigned.TryGetValue(accountId, out pair!);
}
