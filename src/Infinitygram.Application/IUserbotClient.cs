using Infinitygram.Domain;

namespace Infinitygram.Application;

/// <summary>Telegram operations, behind one interface.
/// Production implementation = WTelegramClient adapter (Infrastructure, P3).
/// Test implementation = FakeUserbotClient (Tests). Engines never talk to
/// Telegram directly, so every engine rule is proven without a live account,
/// and the live adapter only has to translate MTProto results into these types.</summary>
public sealed record MemberPage(IReadOnlyList<string> MemberIds, long TotalMembers, bool HasMore);
public sealed record InviteOutcome(bool Ok, string? ErrorCode, int FloodWaitSeconds);

public interface IUserbotClient
{
    Task<MemberPage> GetMembersPageAsync(string groupId, long offset, int limit, CancellationToken ct);
    Task<InviteOutcome> InviteAsync(string accountId, string memberId, CancellationToken ct);
    Task<bool> IsMemberAsync(string groupId, string memberId, CancellationToken ct);
}
