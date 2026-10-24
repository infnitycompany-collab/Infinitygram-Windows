using System.Security.Cryptography;
using Infinitygram.Application;
using Infinitygram.Domain;
using Infinitygram.Infrastructure;

namespace Infinitygram.Tests;

public class VaultAndPolicyTests
{
    [Fact]
    public void Vault_RoundTrip_ReturnsPlaintext_AndAuditsBothOperations()
    {
        var audit = new InMemoryAuditStore();
        var vault = new VaultService(audit);
        var sealedData = vault.Encrypt("test-master-key", "my-secret-session", "acc-1");
        Assert.Equal("my-secret-session", vault.Decrypt("test-master-key", sealedData, "acc-1"));
        Assert.Equal(2, audit.All().Count);
        Assert.DoesNotContain(audit.All(), e => (e.Detail ?? "").Contains("secret"));
    }

    [Fact]
    public void Vault_TamperedCiphertext_FailsAuthentication()
    {
        var vault = new VaultService(new InMemoryAuditStore());
        var sealedData = vault.Encrypt("k", "hello", "acc-1");
        sealedData[^1] ^= 0xFF;
        Assert.Throws<AuthenticationTagMismatchException>(() => vault.Decrypt("k", sealedData, "acc-1"));
    }

    [Fact]
    public void Vault_WrongKey_FailsAuthentication()
    {
        var vault = new VaultService(new InMemoryAuditStore());
        var sealedData = vault.Encrypt("correct", "hello", "acc-1");
        Assert.Throws<AuthenticationTagMismatchException>(() => vault.Decrypt("wrong", sealedData, "acc-1"));
    }

    [Theory]
    [InlineData(9980, 9681)] // the documented live case: 9980 -> ceil(9980*97/100)
    [InlineData(10000, 9700)]
    [InlineData(100, 97)]
    [InlineData(1, 1)]
    public void Coverage_Threshold_IsCeil97(long total, long expected)
    {
        Assert.Equal(expected, CoveragePolicy.ThresholdFor(total));
        Assert.False(CoveragePolicy.IsComplete(expected, total) && total > expected);
    }

    [Fact]
    public void Coverage_IsComplete_OnlyAt100Percent()
    {
        Assert.True(CoveragePolicy.HasReachedThreshold(9681, 9980));
        Assert.False(CoveragePolicy.IsComplete(9681, 9980));
        Assert.True(CoveragePolicy.IsComplete(9980, 9980));
    }

    [Fact]
    public void Quota_BlocksAtLimits()
    {
        Assert.True(QuotaPolicy.CanAttempt(49, 199));
        Assert.False(QuotaPolicy.CanAttempt(50, 10));
        Assert.False(QuotaPolicy.CanAttempt(10, 200));
        Assert.Equal(20, QuotaPolicy.AddChunkSize);
    }
}

public class SecretRedactorTests
{
    [Fact]
    public void Redact_RemovesApiHash_AndMasksPhone()
    {
        const string apiHash = "0123456789abcdef0123456789abcdef";
        var text = $"connect api_hash={apiHash} phone +201234567890 done";
        var clean = SecretRedactor.Redact(text, apiHash);
        Assert.DoesNotContain(apiHash, clean);
        Assert.DoesNotContain("+201234567890", clean);
        Assert.Contains("done", clean);
    }

    [Fact]
    public void Redact_LeavesNormalText_Unchanged()
    {
        Assert.Equal("extraction finished 100 ok", SecretRedactor.Redact("extraction finished 100 ok"));
    }

    [Fact]
    public void MaskPhone_KeepsOnlyLastTwoDigits()
    {
        Assert.Equal("***90", SecretRedactor.MaskPhone("+201234567890"));
    }
}

public class HeavyBrainTests
{
    [Fact]
    public void ErrorPolicy_PeerFlood_Cooldown3600_AndOthersContinue()
    {
        var d = TelegramErrorPolicy.Classify("PEER_FLOOD");
        Assert.Equal(ErrorAction.CooldownAccount, d.Action);
        Assert.Equal(3600, d.CooldownSeconds);
    }

    [Fact]
    public void ErrorPolicy_UserChannelsTooMuch_SkipsAccount_NotJob()
    {
        var d = TelegramErrorPolicy.Classify("USER_CHANNELS_TOO_MUCH");
        Assert.Equal(ErrorAction.SkipAccount, d.Action);
        Assert.NotEqual(ErrorAction.AbortJob, d.Action);
    }

    [Fact]
    public void ErrorPolicy_FloodWait_UsesServerSeconds_AccountOnly()
    {
        var d = TelegramErrorPolicy.Classify("FLOOD_WAIT", 42);
        Assert.Equal(ErrorAction.PauseAccount, d.Action);
        Assert.Equal(42, d.CooldownSeconds);
    }

    [Fact]
    public void ErrorPolicy_Privacy_SkipsMember_AndPrivate_AbortsJob()
    {
        Assert.Equal(ErrorAction.SkipMember, TelegramErrorPolicy.Classify("USER_PRIVACY_RESTRICTED").Action);
        Assert.Equal(ErrorAction.AbortJob, TelegramErrorPolicy.Classify("CHANNEL_PRIVATE").Action);
        Assert.Equal(ErrorAction.ReloginAccount, TelegramErrorPolicy.Classify("SESSION_REVOKED").Action);
    }

    [Fact]
    public void RoundRobin_OneByOne_SamePace_AndStuckSkipped_ToTheEnd()
    {
        var pool = new RoundRobinPool(["a", "b", "c"]);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("a", pool.TryNext(now));
        Assert.Equal("b", pool.TryNext(now));
        pool.MarkStuck("c"); // c gets stuck mid-job: skip it, never block
        Assert.Equal("a", pool.TryNext(now));
        Assert.Equal("b", pool.TryNext(now));
        Assert.Equal("a", pool.TryNext(now));
        Assert.Contains("c", pool.StuckAccounts);
        Assert.Equal(2, pool.AvailableCount(now));
    }

    [Fact]
    public void RoundRobin_Cooldown_AccountReturnsAfterExpiry()
    {
        var pool = new RoundRobinPool(["a", "b"]);
        var now = DateTimeOffset.UtcNow;
        pool.MarkCooldown("a", now.AddSeconds(60));
        Assert.Equal("b", pool.TryNext(now));
        // After expiry the pointer is back at a: a returns first, then b resumes the pace.
        Assert.Equal("a", pool.TryNext(now.AddSeconds(61)));
        Assert.Equal("b", pool.TryNext(now.AddSeconds(61)));
    }

    [Fact]
    public void ToolFile_PerAccount_PerGroup_FullPullNeverClaimsPartialAsComplete()
    {
        Assert.Equal("tools/acc1/MyGroup.csv", ToolFiles.PathFor("acc1", "MyGroup"));
        // Callers must pass the internal account id (acc1), never a phone number:
        // filenames carry ids only, per SECURITY-CLOSURE.md.
        Assert.Equal("tools/acc1/My_Group.csv", ToolFiles.PathFor("acc1", "My Group"));
        var route = new ExtractionRoute(Guid.NewGuid(), "acc1", "123", "MyGroup", 8600, 8600, 9980);
        Assert.Equal("Partial", route.FileStatus);
        Assert.False(route.IsFullPull);
        var full = route.Advance(9980, 9980);
        Assert.True(full.IsFullPull);
        Assert.Equal("Complete", full.FileStatus);
        Assert.Equal(route.ToolPath, full.ToolPath); // same file, same route, no reread
    }

    [Fact]
    public void ApiPool_Duplicates_AreHardErrors_NotWarnings()
    {
        var hash = new string('a', 32);
        var hash2 = new string('b', 32);
        var (pool, errors) = ApiCredentialPool.ParseTxt($"111,{hash}\n111,{hash2}\n222,{hash}\n");
        Assert.Equal(1, pool.PairCount); // only the first valid line survives; both duplicates rejected
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("duplicate"));
    }

    [Fact]
    public void ApiPool_Txt500_AssignsInOrder_ThenRepeatsSameOrder()
    {
        var lines = Enumerable.Range(1, 500)
            .Select(i => $"{1000 + i},{i.ToString("x").PadLeft(32, '0')}");
        var (pool, errors) = ApiCredentialPool.ParseTxt(string.Join("\n", lines));
        Assert.Empty(errors);
        Assert.Equal(500, pool.PairCount);
        var first = pool.Assign("acc-1");
        Assert.Equal(1001, first.ApiId);
        Assert.Equal(1002, pool.Assign("acc-2").ApiId);
        Assert.Same(first, pool.Assign("acc-1")); // one pair per account, stable
        for (var i = 3; i <= 500; i++) pool.Assign($"acc-{i}");
        var repeated = pool.Assign("acc-501");
        Assert.Equal(1001, repeated.ApiId); // exhausted -> repeat same order
        Assert.Equal(1, pool.CycleCount);
    }

    [Fact]
    public void ApiPool_InvalidLines_Reported_WithLineNumbers()
    {
        var (_, errors) = ApiCredentialPool.ParseTxt("notanumber,abc\n999,short\n");
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("Line 1"));
        Assert.Contains(errors, e => e.Contains("Line 2"));
    }
}
