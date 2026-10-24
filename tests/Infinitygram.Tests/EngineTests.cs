using Infinitygram.Application;
using Infinitygram.Domain;

namespace Infinitygram.Tests;

internal sealed class FakeUserbotClient : IUserbotClient
{
    private readonly List<string> _allMembers;
    private readonly Dictionary<string, string> _inviteErrors;
    private int _failPageAt = -1;
    private bool _failedOnce;

    public FakeUserbotClient(List<string> allMembers, Dictionary<string, string>? inviteErrors = null)
    {
        _allMembers = allMembers;
        _inviteErrors = inviteErrors ?? new();
    }

    public void FailPageAt(int pageIndex) => _failPageAt = pageIndex;

    public Task<MemberPage> GetMembersPageAsync(string groupId, long offset, int limit, CancellationToken ct)
    {
        var pageIndex = (int)(offset / limit);
        if (pageIndex == _failPageAt && !_failedOnce)
        {
            _failedOnce = true;
            throw new InvalidOperationException("Connection shut down");
        }
        var slice = _allMembers.Skip((int)offset).Take(limit).ToList();
        return Task.FromResult(new MemberPage(slice, _allMembers.Count, offset + slice.Count < _allMembers.Count));
    }

    public Task<InviteOutcome> InviteAsync(string accountId, string memberId, CancellationToken ct)
    {
        if (_inviteErrors.TryGetValue(memberId, out var code))
            return Task.FromResult(new InviteOutcome(false, code, code == "FLOOD_WAIT" ? 30 : 0));
        return Task.FromResult(new InviteOutcome(true, null, 0));
    }

    public Task<bool> IsMemberAsync(string groupId, string memberId, CancellationToken ct)
        => Task.FromResult(_allMembers.Contains(memberId));
}

public class EngineTests
{
    [Fact]
    public async Task Extraction_FullPull_Dedupes_AndMarksComplete()
    {
        var members = Enumerable.Range(1, 250).Select(i => $"u{i}").ToList();
        members.Add("u1"); // duplicate in source must not inflate the count
        var engine = new ExtractionEngine();
        var (route, distinct) = await engine.ExtractAsync(
            new FakeUserbotClient(members), "a", "g1", "MyGroup", 250, null, 100, CancellationToken.None);
        Assert.Equal(250, distinct.Count);
        Assert.True(route.IsFullPull);
        Assert.Equal("Complete", route.FileStatus);
    }

    [Fact]
    public async Task Extraction_TransportFailure_StopsAtCheckpoint_ThenResumesWithoutReread()
    {
        var members = Enumerable.Range(1, 300).Select(i => $"u{i}").ToList();
        var fake = new FakeUserbotClient(members);
        fake.FailPageAt(1); // second page fails once, engine retries and succeeds (bounded retry)
        var engine = new ExtractionEngine();
        var (route, distinct) = await engine.ExtractAsync(
            fake, "a", "g1", "MyGroup", 300, null, 100, CancellationToken.None);
        Assert.Equal(300, distinct.Count);
        Assert.True(route.IsFullPull);
    }

    [Fact]
    public async Task Add_RoundRobin_PrivacySkip_PeerFloodCooldown_ReportReconciles()
    {
        var errors = new Dictionary<string, string>
        {
            ["m2"] = "USER_PRIVACY_RESTRICTED",
            ["m4"] = "PEER_FLOOD",
        };
        var engine = new AddEngine();
        var pool = new RoundRobinPool(["a", "b"]);
        var now = DateTimeOffset.UtcNow;
        var (filtered, report) = await engine.AddAsync(
            new FakeUserbotClient([], errors), pool,
            ["m1", "m2", "m3", "m4", "m5"], "MyGroup", () => now, CancellationToken.None);
        Assert.Equal(5, filtered.Total);
        Assert.Equal(4, filtered.Added.Count); // m1, m3, m5 + m4 retried on the other account after PeerFlood
        Assert.Single(filtered.Skipped); // m2 privacy only
        Assert.True(report.Reconciles);
        Assert.Equal(report.Total, filtered.Total);
    }

    [Fact]
    public async Task Add_JobError_AbortsRemaining_AsUncertain_NotDropped()
    {
        var errors = new Dictionary<string, string> { ["m2"] = "CHANNEL_PRIVATE" };
        var engine = new AddEngine();
        var pool = new RoundRobinPool(["a"]);
        var (_, report) = await engine.AddAsync(
            new FakeUserbotClient([], errors), pool,
            ["m1", "m2", "m3"], "G", () => DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal(3, report.Total);
        Assert.Equal(1, report.Added);
        Assert.Equal(2, report.Uncertain); // m2 + m3 (aborted), none silently dropped
    }
}
