using System.Text.RegularExpressions;

namespace Infinitygram.Application;

/// <summary>Remaining modules, real logic, no Telegram calls:
/// Filter, Username Lab, Proxies, Throttling, Reader (code extraction),
/// Dashboard summary. Every rule here is unit-proven; live Telegram
/// lookups (availability, proxy reachability) are P3-live only.</summary>
public sealed record MemberInfo(string Id, string? Username, string? Phone, bool HasTelegram);

public static class MemberFilter
{
    public static (IReadOnlyList<MemberInfo> WithTelegram, IReadOnlyList<MemberInfo> WithoutTelegram,
        IReadOnlyList<MemberInfo> WithUsername, IReadOnlyList<MemberInfo> WithPhone)
        Split(IEnumerable<MemberInfo> members)
    {
        var list = members.ToList();
        return (
            list.Where(m => m.HasTelegram).ToList(),
            list.Where(m => !m.HasTelegram).ToList(),
            list.Where(m => !string.IsNullOrWhiteSpace(m.Username)).ToList(),
            list.Where(m => !string.IsNullOrWhiteSpace(m.Phone)).ToList());
    }
}

public static class UsernameGenerator
{
    // Telegram usernames: 5-32 chars, a-z 0-9 _, must start with a letter.
    private static readonly Regex Valid = new("^[a-z][a-z0-9_]{4,31}$", RegexOptions.Compiled);

    public static bool IsValid(string username) => Valid.IsMatch(username);

    public static IReadOnlyList<string> Generate(string prefix, int count, int length, Random? rng = null)
    {
        if (length < 5 || length > 32) throw new ArgumentOutOfRangeException(nameof(length));
        rng ??= new Random();
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789_";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        var guard = 0;
        while (result.Count < count && guard++ < count * 100)
        {
            var suffix = new string(Enumerable.Range(0, Math.Max(0, length - prefix.Length))
                .Select(_ => chars[rng.Next(chars.Length)]).ToArray());
            var candidate = (prefix + suffix).ToLowerInvariant();
            if (candidate.Length != length || !IsValid(candidate) || !seen.Add(candidate)) continue;
            result.Add(candidate);
        }
        return result;
    }
}

public sealed record ProxyInfo(string Address, int Port, string? Username, string? Password)
{
    public string Key => $"{Address}:{Port}";
}

public static class ProxyPool
{
    public static ProxyInfo? Parse(string line)
    {
        // Forms: host:port  |  host:port:user:pass  |  user:pass@host:port
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#')) return null;
        try
        {
            if (text.Contains('@'))
            {
                var at = text.Split('@');
                var creds = at[0].Split(':');
                var host = at[1].Split(':');
                return new ProxyInfo(host[0], int.Parse(host[1]),
                    creds.Length > 0 ? creds[0] : null, creds.Length > 1 ? creds[1] : null);
            }
            var parts = text.Split(':');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var port) || port is < 1 or > 65535) return null;
            return new ProxyInfo(parts[0], port,
                parts.Length > 2 ? parts[2] : null, parts.Length > 3 ? parts[3] : null);
        }
        catch { return null; }
    }

    // Round-robin rotation, skipping proxies that failed 3+ times in a row.
    public static ProxyInfo? Next(IReadOnlyList<ProxyInfo> proxies, ref int pointer, IReadOnlyDictionary<string, int> consecutiveFailures)
    {
        if (proxies.Count == 0) return null;
        for (var i = 0; i < proxies.Count; i++)
        {
            var p = proxies[pointer % proxies.Count];
            pointer++;
            if (consecutiveFailures.GetValueOrDefault(p.Key) < 3) return p;
        }
        return null;
    }
}

public sealed record ThrottlePlan(int DelaySecondsAfterMessage, int RestAfterMessages, int RestSeconds, int RestAfterAddFailures, int RestSecondsAfterAddFailures)
{
    public bool IsValid => DelaySecondsAfterMessage >= 0 && RestAfterMessages > 0 && RestSeconds >= 0
        && RestAfterAddFailures > 0 && RestSecondsAfterAddFailures >= 0;

    // How many messages can be sent in a window before a rest is forced?
    public int MessagesBeforeRest => RestAfterMessages;
    public TimeSpan TotalRestFor(int messagesSent)
        => TimeSpan.FromSeconds((messagesSent / RestAfterMessages) * RestSeconds);
}

public static class CodeExtractor
{
    // Telegram login codes are 5 digits; extract them from message text
    // (reader module). Never returns a code longer than 5 digits, never
    // invents one when the text has none.
    private static readonly Regex Code = new(@"\b(\d{5})\b", RegexOptions.Compiled);
    public static IReadOnlyList<string> ExtractCodes(string text)
        => Code.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
}

public sealed record DashboardSummary(int Accounts, int Reports, int Added, int Failed, int Uncertain)
{
    public int TotalOperations => Added + Failed + Uncertain;
    public string ToCleanText()
        => $"accounts: {Accounts} | reports: {Reports} | added: {Added} | failed: {Failed} | uncertain: {Uncertain} | total: {TotalOperations}";
}
