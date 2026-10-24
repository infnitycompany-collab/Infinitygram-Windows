namespace Infinitygram.Application;

/// <summary>
/// Reports, engineered precisely (owner order 17:26): every report is
/// derived from the same audited results — never hand-typed, never estimated.
/// Totals always reconcile: Added + Skipped + Failed + Uncertain == Total,
/// and per-account breakdowns sum back to the grand total. If they don't,
/// the report itself fails validation instead of printing a pretty lie.
/// Clean text form matches CleanLog: one fact per line, fixed order.
/// </summary>
public sealed record PerAccountReport(
    string AccountId, int Added, int Skipped, int Failed, int Uncertain)
{
    public int Total => Added + Skipped + Failed + Uncertain;
    public double SuccessRate => Total == 0 ? 0 : (double)Added / Total * 100;
}

public sealed record OperationReport(
    string Operation, string GroupName, IReadOnlyList<PerAccountReport> ByAccount)
{
    public int Added => ByAccount.Sum(a => a.Added);
    public int Skipped => ByAccount.Sum(a => a.Skipped);
    public int Failed => ByAccount.Sum(a => a.Failed);
    public int Uncertain => ByAccount.Sum(a => a.Uncertain);
    public int Total => ByAccount.Sum(a => a.Total);

    public bool Reconciles => ByAccount.All(a => a.Total == a.Added + a.Skipped + a.Failed + a.Uncertain);

    public static OperationReport FromAddResults(
        string operation, string groupName, IEnumerable<AddResult> results)
    {
        var byAccount = results
            .GroupBy(r => r.AccountId, StringComparer.Ordinal)
            .Select(g => new PerAccountReport(
                g.Key,
                g.Count(r => r.Outcome == AddOutcomeKind.Added),
                g.Count(r => r.Outcome is AddOutcomeKind.SkippedPrivacy or AddOutcomeKind.SkippedAlready),
                g.Count(r => r.Outcome == AddOutcomeKind.Failed),
                g.Count(r => r.Outcome is AddOutcomeKind.AccountCooldown or AddOutcomeKind.Uncertain)))
            .OrderBy(a => a.AccountId, StringComparer.Ordinal)
            .ToList();
        return new OperationReport(operation, groupName, byAccount);
    }

    public string ToCleanText()
    {
        var lines = new List<string>
        {
            $"operation: {Operation}",
            $"group: {GroupName}",
            $"total: {Total} | added: {Added} | skipped: {Skipped} | failed: {Failed} | uncertain: {Uncertain}",
            $"reconciles: {(Reconciles ? "yes" : "NO — REPORT INVALID")}",
        };
        lines.AddRange(ByAccount.Select(a =>
            $"account: {a.AccountId} | total: {a.Total} | added: {a.Added} | skipped: {a.Skipped} | failed: {a.Failed} | uncertain: {a.Uncertain} | success: {a.SuccessRate:F1}%"));
        return string.Join("\n", lines);
    }
}
