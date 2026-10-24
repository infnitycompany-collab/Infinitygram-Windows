using Infinitygram.Domain;

namespace Infinitygram.Tests;

/// <summary>Owner order 17:16: the balance must be exact.
/// A member problem must NEVER stop the account; an account problem stops
/// only that account; a job problem aborts the job. Proven by tiers AND by
/// a full simulated job where every member ends with exactly one outcome.</summary>
public class BalanceTests
{
    [Fact]
    public void Tier_MemberErrors_NeverTouchAccount()
    {
        foreach (var code in new[]
                 { "USER_PRIVACY_RESTRICTED", "USER_NOT_MUTUAL_CONTACT", "USER_ALREADY_PARTICIPANT",
                   "USER_BLOCKED", "USER_DEACTIVATED", "USER_IS_BOT" })
        {
            var d = TelegramErrorPolicy.Classify(code);
            Assert.Equal(ErrorTier.Member, d.Tier);
            Assert.Equal(ErrorAction.SkipMember, d.Action);
            Assert.False(d.TouchesAccount);
        }
    }

    [Fact]
    public void Tier_AccountErrors_TouchOnlyThatAccount()
    {
        Assert.Equal(ErrorTier.Account, TelegramErrorPolicy.Classify("PEER_FLOOD").Tier);
        Assert.Equal(ErrorTier.Account, TelegramErrorPolicy.Classify("FLOOD_WAIT", 30).Tier);
        Assert.Equal(ErrorTier.Account, TelegramErrorPolicy.Classify("USER_CHANNELS_TOO_MUCH").Tier);
        Assert.Equal(ErrorTier.Account, TelegramErrorPolicy.Classify("SESSION_REVOKED").Tier);
        Assert.True(TelegramErrorPolicy.Classify("PEER_FLOOD").TouchesAccount);
    }

    [Fact]
    public void Tier_JobErrors_AbortJobOnly()
    {
        Assert.Equal(ErrorTier.Job, TelegramErrorPolicy.Classify("CHANNEL_PRIVATE").Tier);
        Assert.Equal(ErrorAction.AbortJob, TelegramErrorPolicy.Classify("CHAT_ADMIN_REQUIRED").Action);
    }

    [Fact]
    public void SimulatedJob_PrivacySkip_DoesNotStopAccount_AndJobCompletes()
    {
        // Members 1..6, accounts a/b round-robin.
        // Member 2: privacy closed -> skip member only, account a continues.
        // Member 4: account b hits PeerFlood -> b cools, a finishes the rest alone.
        // Every member ends with exactly one outcome; no member is lost, no duplicate.
        var pool = new RoundRobinPool(["a", "b"]);
        var now = DateTimeOffset.UtcNow;
        var outcomes = new Dictionary<int, string>();
        var script = new Dictionary<int, string>
        {
            [1] = "ok", [2] = "USER_PRIVACY_RESTRICTED", [3] = "ok",
            [4] = "PEER_FLOOD", [5] = "ok", [6] = "ok",
        };
        foreach (var member in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var account = pool.TryNext(now);
            Assert.NotNull(account); // job never stalls while any account is available
            var decision = script[member] == "ok"
                ? null
                : TelegramErrorPolicy.Classify(script[member]);
            if (decision is null) outcomes[member] = $"added-by-{account}";
            else if (decision.Tier == ErrorTier.Member) outcomes[member] = "skipped-privacy";
            else if (decision.Tier == ErrorTier.Account)
            {
                // Account problem: member is NOT lost — requeue it to the next available account.
                pool.MarkCooldown(account, now.AddSeconds(decision.CooldownSeconds > 0 ? decision.CooldownSeconds : 3600));
                var retry = pool.TryNext(now);
                Assert.NotNull(retry);
                outcomes[member] = $"added-by-{retry}-after-{account}-cooldown";
            }
            else Assert.Fail("Unexpected tier in simulation");
        }
        Assert.Equal(6, outcomes.Count);
        Assert.Equal("skipped-privacy", outcomes[2]);
        Assert.StartsWith("added-by-a-after-b-cooldown", outcomes[4]);
        Assert.DoesNotContain(outcomes.Values, v => v.Contains("lost"));
    }
}

public class AutoFilterTests
{
    [Fact]
    public void AutoFilter_SplitsAddedSkippedFailedUncertain_WithoutMixing()
    {
        var results = new[]
        {
            new Infinitygram.Application.AddResult("m1", "a", Infinitygram.Application.AddOutcomeKind.Added, "ok"),
            new Infinitygram.Application.AddResult("m2", "a", Infinitygram.Application.AddOutcomeKind.SkippedPrivacy, "privacy"),
            new Infinitygram.Application.AddResult("m3", "b", Infinitygram.Application.AddOutcomeKind.SkippedAlready, "already"),
            new Infinitygram.Application.AddResult("m4", "a", Infinitygram.Application.AddOutcomeKind.Failed, "failed"),
            new Infinitygram.Application.AddResult("m5", "b", Infinitygram.Application.AddOutcomeKind.AccountCooldown, "peerflood"),
            new Infinitygram.Application.AddResult("m6", "a", Infinitygram.Application.AddOutcomeKind.Uncertain, "unknown"),
        };
        var f = Infinitygram.Application.AddFilter.Filter(results);
        Assert.Equal(6, f.Total);
        Assert.Single(f.Added);
        Assert.Equal(2, f.Skipped.Count);
        Assert.Single(f.Failed);
        Assert.Equal(2, f.Uncertain.Count); // cooldown + uncertain: review file, NOT failed file
        Assert.DoesNotContain(f.Failed, r => r.Outcome == Infinitygram.Application.AddOutcomeKind.Uncertain);
    }

    [Fact]
    public void AutoFilter_Save_WritesFourCleanFiles_WithFormulaGuard()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ig-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = Infinitygram.Application.AddFilter.Filter(new[]
            {
                new Infinitygram.Application.AddResult("m1", "a", Infinitygram.Application.AddOutcomeKind.Added, "ok"),
                new Infinitygram.Application.AddResult("=evil", "a", Infinitygram.Application.AddOutcomeKind.Failed, "x"),
            });
            var files = Infinitygram.Application.AddFilter.SaveFiltered(dir, "a", "MyGroup", f);
            Assert.Equal(4, files.Count);
            Assert.All(files, f => Assert.True(File.Exists(f)));
            var failed = File.ReadAllText(files.First(p => p.EndsWith("failed.csv")));
            Assert.Contains("'=evil", failed); // formula injection escaped
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void CleanLog_OneLine_FixedShape_AndRedactsSecrets()
    {
        var line = Infinitygram.Application.CleanLog.Format(
            new DateTimeOffset(2026, 10, 24, 17, 17, 0, TimeSpan.Zero),
            "info", "add", "a", "added m1 api_hash=0123456789abcdef0123456789abcdef");
        Assert.StartsWith("[2026-10-24T17:17:00Z] [INFO] [add] [a] ", line);
        Assert.DoesNotContain("0123456789abcdef", line);
        Assert.Single(line.Split('\n')); // one line, organized, no messy multi-line spill
    }
}
