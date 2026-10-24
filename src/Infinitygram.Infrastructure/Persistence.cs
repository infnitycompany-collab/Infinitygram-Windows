using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Infinitygram.Application;

namespace Infinitygram.Infrastructure;

/// <summary>Durable SQLite rows. Secrets rule, enforced by construction:
/// api_hash is NEVER stored plain — only Vault-sealed bytes (SealedApiHash).
/// Session files are NOT in this database at all (see SECURITY-CLOSURE.md).
/// Audit Detail must already be redacted by the caller (SecretRedactor).</summary>
public sealed class AccountRow
{
    [Key] public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long ApiId { get; set; }
    public byte[] SealedApiHash { get; set; } = Array.Empty<byte>();
    public string Status { get; set; } = "Cold";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class AuditRow
{
    [Key] public Guid Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string AccountId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Target { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string? Detail { get; set; }
}

public sealed class ExtractionJobRow
{
    [Key] public Guid JobId { get; set; }
    public string AccountId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public long CheckpointOffset { get; set; }
    public long DistinctFetched { get; set; }
    public long TotalMembers { get; set; }
    public string FileStatus { get; set; } = "Partial";
    public string ToolPath { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class InfinitygramDbContext : DbContext
{
    public InfinitygramDbContext(DbContextOptions<InfinitygramDbContext> options) : base(options) { }
    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();
    public DbSet<ExtractionJobRow> ExtractionJobs => Set<ExtractionJobRow>();

    public static InfinitygramDbContext ForFile(string path)
    {
        var options = new DbContextOptionsBuilder<InfinitygramDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var db = new InfinitygramDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}

/// <summary>Account store: add/list/delete + vault-sealed api_hash.
/// Delete removes the row AND writes an audit row; it never leaves a sealed
/// hash orphaned in a backup — backups of this DB are the operator's
/// responsibility and are covered by the retention rules in the plan.</summary>
public sealed class AccountStore
{
    private readonly InfinitygramDbContext _db;
    private readonly VaultService _vault;

    public AccountStore(InfinitygramDbContext db, VaultService vault)
    {
        _db = db;
        _vault = vault;
    }

    public AccountRow AddAccount(string accountId, string displayName, long apiId, string apiHashPlain, string masterKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountId);
        if (_db.Accounts.Any(a => a.Id == accountId))
            throw new InvalidOperationException($"Account '{accountId}' already exists.");
        if (_db.Accounts.Any(a => a.ApiId == apiId))
            throw new InvalidOperationException($"api_id {apiId} is already assigned to another account — one pair per account.");
        var sealedHash = _vault.Encrypt(masterKey, apiHashPlain, accountId);
        var row = new AccountRow
        {
            Id = accountId, DisplayName = displayName, ApiId = apiId,
            SealedApiHash = sealedHash, Status = "Cold", CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _db.Accounts.Add(row);
        _db.AuditEntries.Add(new AuditRow
        {
            Id = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow, AccountId = accountId,
            Operation = "account.add", Target = "accounts", Outcome = "ok",
        });
        _db.SaveChanges();
        return row;
    }

    public string RevealApiHash(string accountId, string masterKey)
    {
        var row = _db.Accounts.Single(a => a.Id == accountId);
        return _vault.Decrypt(masterKey, row.SealedApiHash, accountId);
    }

    public IReadOnlyList<AccountRow> ListAccounts()
        => _db.Accounts.OrderBy(a => a.Id).ToList();

    public void DeleteAccount(string accountId)
    {
        var row = _db.Accounts.Single(a => a.Id == accountId);
        _db.Accounts.Remove(row);
        _db.AuditEntries.Add(new AuditRow
        {
            Id = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow, AccountId = accountId,
            Operation = "account.delete", Target = "accounts", Outcome = "ok",
        });
        _db.SaveChanges();
    }

    public void UpsertExtractionJob(Domain.ExtractionRoute route)
    {
        var existing = _db.ExtractionJobs.SingleOrDefault(j => j.JobId == route.JobId);
        if (existing is null)
        {
            _db.ExtractionJobs.Add(new ExtractionJobRow
            {
                JobId = route.JobId, AccountId = route.AccountId, GroupId = route.GroupId,
                GroupName = route.GroupName, CheckpointOffset = route.CheckpointOffset,
                DistinctFetched = route.DistinctFetched, TotalMembers = route.TotalMembers,
                FileStatus = route.FileStatus, ToolPath = route.ToolPath,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.CheckpointOffset = route.CheckpointOffset;
            existing.DistinctFetched = route.DistinctFetched;
            existing.FileStatus = route.FileStatus;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        _db.SaveChanges();
    }
}
