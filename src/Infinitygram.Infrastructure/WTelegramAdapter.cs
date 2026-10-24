using Infinitygram.Application;

namespace Infinitygram.Infrastructure;

/// <summary>
/// Live Telegram adapter — SKELETON in this phase, honestly marked.
/// The engines are proven against IUserbotClient; this adapter is the ONLY
/// class that will talk to WTelegramClient (package 4.3.4, already restored).
/// It is NOT wired to a live session yet, because wiring it requires the
/// owner's own api_id/api_hash + a login (phone/OTP/2FA/QR) on his Windows
/// machine, and no leaked credential will ever be used here.
/// Until then every call fails fast with a clear message instead of
/// pretending to be live — a fake live adapter would be worse than none.
/// Wiring checklist (P3-live, on the owner's machine):
///   1. Create WTelegram Client with a config callback returning api_id,
///      api_hash, phone_number, verification_code, password (2FA) — all
///      from the Vault, never from plain text.
///   2. Map GetMembersPage -> channels.GetParticipants with offset/limit,
///      dedupe by peer id, total = reported group size.
///   3. Map Invite -> channels.InviteToChannel, translating MTProto errors
///      to the exact codes in TelegramErrorPolicy (FLOOD_WAIT seconds,
///      PEER_FLOOD, USER_PRIVACY_RESTRICTED, USER_CHANNELS_TOO_MUCH, ...).
///   4. Never log api_hash, tokens, OTPs or full phones (SecretRedactor).
/// </summary>
public sealed class WTelegramUserbotClient : IUserbotClient
{
    public Task<MemberPage> GetMembersPageAsync(string groupId, long offset, int limit, CancellationToken ct)
        => Task.FromException<MemberPage>(new InvalidOperationException(
            "Live Telegram is not configured: add an account (api_id/api_hash) and complete login on the owner's Windows machine first."));

    public Task<InviteOutcome> InviteAsync(string accountId, string memberId, CancellationToken ct)
        => Task.FromException<InviteOutcome>(new InvalidOperationException(
            "Live Telegram is not configured: add an account (api_id/api_hash) and complete login on the owner's Windows machine first."));

    public Task<bool> IsMemberAsync(string groupId, string memberId, CancellationToken ct)
        => Task.FromException<bool>(new InvalidOperationException(
            "Live Telegram is not configured: add an account (api_id/api_hash) and complete login on the owner's Windows machine first."));
}
