using Infinitygram.Application;
using Infinitygram.Domain;
using Infinitygram.Infrastructure;

namespace Infinitygram.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly InfinitygramDbContext _db;

    public PersistenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "ig-test-" + Guid.NewGuid().ToString("N") + ".db");
        _db = InfinitygramDbContext.ForFile(_dbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void AccountStore_SealsApiHash_AndRevealsOnlyWithMasterKey()
    {
        var vault = new VaultService(new InMemoryAuditStore());
        var store = new AccountStore(_db, vault);
        var hash = new string('c', 32);
        var row = store.AddAccount("acc1", "Account 1", 123456, hash, "master-key");
        Assert.NotEqual(hash, Convert.ToHexString(row.SealedApiHash)); // never plain
        Assert.Equal(hash, store.RevealApiHash("acc1", "master-key"));
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => store.RevealApiHash("acc1", "wrong-key"));
        Assert.Single(store.ListAccounts());
    }

    [Fact]
    public void AccountStore_DuplicateApiId_IsRejected()
    {
        var vault = new VaultService(new InMemoryAuditStore());
        var store = new AccountStore(_db, vault);
        store.AddAccount("acc1", "A1", 111, new string('a', 32), "k");
        Assert.Throws<InvalidOperationException>(() =>
            store.AddAccount("acc2", "A2", 111, new string('b', 32), "k"));
        Assert.Single(store.ListAccounts());
    }

    [Fact]
    public void AccountStore_Delete_RemovesRow_AndAudits()
    {
        var vault = new VaultService(new InMemoryAuditStore());
        var store = new AccountStore(_db, vault);
        store.AddAccount("acc1", "A1", 111, new string('a', 32), "k");
        store.DeleteAccount("acc1");
        Assert.Empty(store.ListAccounts());
        Assert.Contains(_db.AuditEntries, a => a.Operation == "account.delete");
    }

    [Fact]
    public void ExtractionJob_Upsert_PersistsCheckpoint_ForResume()
    {
        var vault = new VaultService(new InMemoryAuditStore());
        var store = new AccountStore(_db, vault);
        var route = new ExtractionRoute(Guid.NewGuid(), "acc1", "g1", "MyGroup", 8600, 8600, 9980);
        store.UpsertExtractionJob(route);
        var resumed = route.Advance(9980, 9980);
        store.UpsertExtractionJob(resumed);
        var saved = _db.ExtractionJobs.Single(j => j.JobId == route.JobId);
        Assert.Equal(9980, saved.CheckpointOffset);
        Assert.Equal("Complete", saved.FileStatus);
        Assert.Equal("tools/acc1/MyGroup.csv", saved.ToolPath);
    }

    [Fact]
    public async Task LiveAdapter_FailsFast_WithClearMessage_NotFakeLive()
    {
        var adapter = new WTelegramUserbotClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.GetMembersPageAsync("g1", 0, 100, CancellationToken.None));
        Assert.Contains("not configured", ex.Message);
    }
}
