namespace Infinitygram.Application;

/// <summary>Differentiator (owner order 17:17): automatic filtering AFTER adding.
/// When an add job finishes, results are auto-split — no manual sorting —
/// and the user can save the filtered data as separate files.
/// Added / Skipped (privacy/already) / Failed / Uncertain are NEVER mixed:
/// an Uncertain result (e.g. account cooled mid-invite) is not counted as
/// failed and is not retried blindly — it goes to its own file for review.</summary>
public enum AddOutcomeKind
{
    Added, SkippedPrivacy, SkippedAlready, Failed, AccountCooldown, Uncertain,
}

public sealed record AddResult(
    string MemberId, string AccountId, AddOutcomeKind Outcome, string Reason);

public sealed record FilteredAddData(
    IReadOnlyList<AddResult> Added,
    IReadOnlyList<AddResult> Skipped,
    IReadOnlyList<AddResult> Failed,
    IReadOnlyList<AddResult> Uncertain)
{
    public int Total => Added.Count + Skipped.Count + Failed.Count + Uncertain.Count;
}

public static class AddFilter
{
    public static FilteredAddData Filter(IEnumerable<AddResult> results)
    {
        var list = results.ToList();
        return new FilteredAddData(
            list.Where(r => r.Outcome == AddOutcomeKind.Added).ToList(),
            list.Where(r => r.Outcome is AddOutcomeKind.SkippedPrivacy or AddOutcomeKind.SkippedAlready).ToList(),
            list.Where(r => r.Outcome == AddOutcomeKind.Failed).ToList(),
            list.Where(r => r.Outcome is AddOutcomeKind.AccountCooldown or AddOutcomeKind.Uncertain).ToList());
    }

    /// <summary>Save the filtered data, one clean file per bucket, under
    /// reports/add-filter/{account}/{group}_{yyyyMMdd-HHmmss}/.
    /// Cells starting with = + - @ are escaped (CSV formula-injection guard).
    /// Member ids only — no phone numbers in filenames or file contents here.</summary>
    public static IReadOnlyList<string> SaveFiltered(
        string baseDirectory, string accountId, string groupName, FilteredAddData data)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(baseDirectory, "reports", "add-filter",
            Domain.ToolFiles.Sanitize(accountId),
            $"{Domain.ToolFiles.Sanitize(groupName)}_{stamp}");
        Directory.CreateDirectory(dir);
        var written = new List<string>();
        foreach (var (name, rows) in new[]
                 { ("added.csv", data.Added), ("skipped.csv", data.Skipped),
                   ("failed.csv", data.Failed), ("uncertain.csv", data.Uncertain) })
        {
            var path = Path.Combine(dir, name);
            var lines = new List<string> { "member_id,account_id,outcome,reason" };
            lines.AddRange(rows.Select(r =>
                $"{Escape(r.MemberId)},{Escape(r.AccountId)},{r.Outcome},{Escape(r.Reason)}"));
            File.WriteAllLines(path, lines);
            written.Add(path);
        }
        return written;
    }

    private static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) return "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

/// <summary>Clean, organized log: one line per event, fixed shape, no secrets.
/// Format: [UTC ISO] [LEVEL] [operation] [account] message
/// Messages pass through SecretRedactor first, so a log line can never leak
/// an api_hash, token, OTP or full phone number even if the caller forgot.</summary>
public static class CleanLog
{
    public static string Format(DateTimeOffset utc, string level, string operation, string accountId, string message)
        => $"[{utc:yyyy-MM-ddTHH:mm:ssZ}] [{level.ToUpperInvariant()}] [{operation}] [{accountId}] {SecretRedactor.Redact(message)}";
}
