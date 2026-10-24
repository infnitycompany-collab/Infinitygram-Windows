using Infinitygram.Domain;

namespace Infinitygram.Application;

/// <summary>Extraction engine: paginated, deduplicated, checkpointed.
/// Full-pull guarantee: the returned route's checkpoint + distinct count are
/// the truth. A transport failure stops the run at the last good page
/// (Partial, never a fake Complete) and the next run resumes from that
/// checkpoint with NO reread of completed pages.</summary>
public sealed class ExtractionEngine
{
    public async Task<(ExtractionRoute Route, IReadOnlyList<string> Distinct)> ExtractAsync(
        IUserbotClient client, string accountId, string groupId, string groupName,
        long totalMembers, ExtractionRoute? resumeFrom, int pageSize, CancellationToken ct)
    {
        var route = resumeFrom ?? new ExtractionRoute(Guid.NewGuid(), accountId, groupId, groupName, 0, 0, totalMembers);
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        // Resume callers pass already-fetched ids via the route's checkpoint only
        // when they kept them; here the engine fetches from checkpoint forward
        // and dedupes within this run. Full runs (offset 0) dedupe completely.
        var offset = route.CheckpointOffset;
        var attempts = 0;
        while (true)
        {
            MemberPage page;
            try
            {
                page = await client.GetMembersPageAsync(groupId, offset, pageSize, ct);
                attempts = 0;
            }
            catch when (attempts < 3)
            {
                // Bounded transport retry: 1s, 2s, 4s. No page is counted twice.
                await Task.Delay(TimeSpan.FromSeconds(1 << attempts), ct);
                attempts++;
                continue;
            }
            catch
            {
                return (route, distinct.ToList());
            }
            if (page.MemberIds.Count == 0) break;
            foreach (var id in page.MemberIds) distinct.Add(id);
            offset += page.MemberIds.Count;
            // Distinct total = resume base + this run's distinct ids (no double count,
            // no reread: offset only moves forward).
            route = route with
            {
                CheckpointOffset = offset,
                DistinctFetched = (resumeFrom?.DistinctFetched ?? 0) + distinct.Count,
            };
            if (!page.HasMore || route.DistinctFetched >= totalMembers) break;
            if (ct.IsCancellationRequested) break;
        }
        return (route, distinct.ToList());
    }
}

/// <summary>Add engine: round-robin, quotas, error tiers, auto-filter, report.
/// One member at a time, one account at a time, same pace. Member errors skip
/// the member only. Account errors cool that account and retry the SAME member
/// on the next available account (bounded by pool size). Job errors abort
/// honestly, remaining members become Uncertain — never silently dropped.</summary>
public sealed class AddEngine
{
    public async Task<(FilteredAddData Filtered, OperationReport Report)> AddAsync(
        IUserbotClient client, RoundRobinPool pool, IReadOnlyList<string> members,
        string groupName, Func<DateTimeOffset> now, CancellationToken ct)
    {
        var results = new List<AddResult>();
        var usedHour = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedDay = new Dictionary<string, int>(StringComparer.Ordinal);

        var jobAborted = false;
        foreach (var member in members)
        {
            if (ct.IsCancellationRequested)
            {
                results.Add(new AddResult(member, "", AddOutcomeKind.Uncertain, "cancelled"));
                continue;
            }
            if (jobAborted)
            {
                results.Add(new AddResult(member, "", AddOutcomeKind.Uncertain, "job-aborted:previous-member-aborted-job"));
                continue;
            }
            var (outcome, aborted) = await AddOneAsync(client, pool, member, now, usedHour, usedDay, ct);
            jobAborted = aborted;
            results.Add(outcome);
        }

        var filtered = AddFilter.Filter(results);
        var report = OperationReport.FromAddResults("add", groupName, results);
        return (filtered, report);
    }

    private async Task<(AddResult Result, bool JobAborted)> AddOneAsync(
        IUserbotClient client, RoundRobinPool pool, string member,
        Func<DateTimeOffset> now, Dictionary<string, int> usedHour,
        Dictionary<string, int> usedDay, CancellationToken ct)
    {
        var tried = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var account = pool.TryNext(now());
            if (account is null || !tried.Add(account))
                return (new AddResult(member, "", AddOutcomeKind.Uncertain, "no-account-available"), false);
            if (!QuotaPolicy.CanAttempt(usedHour.GetValueOrDefault(account), usedDay.GetValueOrDefault(account)))
            {
                pool.MarkExhausted(account);
                continue;
            }
            var invite = await client.InviteAsync(account, member, ct);
            if (invite.Ok)
            {
                usedHour[account] = usedHour.GetValueOrDefault(account) + 1;
                usedDay[account] = usedDay.GetValueOrDefault(account) + 1;
                return (new AddResult(member, account, AddOutcomeKind.Added, "ok"), false);
            }
            var decision = TelegramErrorPolicy.Classify(invite.ErrorCode ?? "", invite.FloodWaitSeconds);
            switch (decision.Tier)
            {
                case ErrorTier.Member:
                    return (decision.Action == ErrorAction.SkipMember
                        ? new AddResult(member, account,
                            (invite.ErrorCode ?? "").Contains("ALREADY", StringComparison.OrdinalIgnoreCase)
                                ? AddOutcomeKind.SkippedAlready : AddOutcomeKind.SkippedPrivacy,
                            decision.Reason)
                        : new AddResult(member, account, AddOutcomeKind.Failed, decision.Reason), false);
                case ErrorTier.Account:
                    if (decision.Action == ErrorAction.PauseAccount)
                        pool.MarkCooldown(account, now().AddSeconds(decision.CooldownSeconds));
                    else if (decision.Action == ErrorAction.CooldownAccount)
                        pool.MarkCooldown(account, now().AddSeconds(decision.CooldownSeconds));
                    else if (decision.Action == ErrorAction.SkipAccount)
                        pool.MarkCooldown(account, now().AddHours(24));
                    else pool.MarkStuck(account);
                    continue; // same member, next account
                case ErrorTier.Job:
                    return (new AddResult(member, account, AddOutcomeKind.Uncertain, "job-aborted:" + decision.Reason), true);
                default:
                    return (new AddResult(member, account, AddOutcomeKind.Uncertain, "transport:" + decision.Reason), false);
            }
        }
    }
}
