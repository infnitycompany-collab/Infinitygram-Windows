namespace Infinitygram.Domain;

/// <summary>What the engine must do when Telegram answers with an error.
/// ORDERING LAW (owner order 2026-10-24 17:16 — this is the balance):
///   Tier 1 MEMBER  — a problem with ONE member (e.g. he closed his privacy)
///                    must NEVER stop the account. Skip that member only,
///                    do NOT touch the pool, do NOT cool the account,
///                    do NOT count it as an account failure. The account
///                    continues clean to the next member immediately.
///   Tier 2 ACCOUNT — a problem with ONE account (FloodWait, PeerFlood,
///                    channel limit, dead session) stops THAT account only.
///                    Other accounts continue the job to the end.
///   Tier 3 JOB     — a problem with the job itself (private chat, not admin,
///                    write forbidden) aborts the whole job, honestly reported.
///   Tier 4 TRANSPORT — transient network: bounded retry, then fail the batch.
/// No error is ever handled "generically": every code maps to exactly one
/// tier + one action, and every action is audited. Proven base: marathon D23
/// (FloodWait = server seconds, PeerFlood = 3600s).</summary>
public enum ErrorTier { Member, Account, Job, Transport }

public enum ErrorAction
{
    /// <summary>Tier 1: skip this member only, account untouched, job continues.</summary>
    SkipMember,
    /// <summary>Tier 2: skip this account for this operation, others continue.</summary>
    SkipAccount,
    /// <summary>Tier 2: cool this account for CooldownSeconds, others continue.</summary>
    CooldownAccount,
    /// <summary>Tier 2: pause only this account for FloodWaitSeconds, others continue.</summary>
    PauseAccount,
    /// <summary>Tier 2: session dead — stop this account until re-login.</summary>
    ReloginAccount,
    /// <summary>Tier 3: fatal for the whole job.</summary>
    AbortJob,
    /// <summary>Tier 4: transient — bounded retry.</summary>
    RetryWithBackoff,
}

public sealed record ErrorDecision(ErrorTier Tier, ErrorAction Action, int CooldownSeconds, string Reason)
{
    /// <summary>True only when the pool/account state must change.
    /// Member decisions return false: the engine must NOT call MarkStuck/
    /// MarkCooldown for them — that was the bug the owner flagged.</summary>
    public bool TouchesAccount => Tier == ErrorTier.Account;
}

public static class TelegramErrorPolicy
{
    public static ErrorDecision Classify(string code, int floodWaitSeconds = 0)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return c switch
        {
            // ---- Tier 2: ACCOUNT ----
            "FLOOD_WAIT" or "FLOODWAIT" or "FLOOD_WAIT_X"
                => new(ErrorTier.Account, ErrorAction.PauseAccount, Math.Max(1, floodWaitSeconds),
                    "FloodWait: wait exactly the server seconds, this account only."),
            "PEER_FLOOD"
                => new(ErrorTier.Account, ErrorAction.CooldownAccount, 3600,
                    "PeerFlood: account flagged for spam — 3600s cooldown, others continue."),
            // Account joined too many channels/supergroups (Telegram limit ~500).
            // It cannot join more until it leaves some: skip it for joins, others continue.
            "USER_CHANNELS_TOO_MUCH"
                => new(ErrorTier.Account, ErrorAction.SkipAccount, 0,
                    "UserChannelsTooMuch: account at channel limit — skip for joins, others continue."),
            "SESSION_REVOKED" or "AUTH_KEY_UNREGISTERED" or "SESSION_EXPIRED"
                => new(ErrorTier.Account, ErrorAction.ReloginAccount, 0,
                    "Session dead: account needs re-login before any further use."),
            // ---- Tier 1: MEMBER — never stops the account ----
            // "He closed his permission" = USER_PRIVACY_RESTRICTED / NOT_MUTUAL_CONTACT.
            // Even in many groups: skip the member, the account keeps working clean.
            "USER_PRIVACY_RESTRICTED" or "USER_NOT_MUTUAL_CONTACT"
            or "USER_ALREADY_PARTICIPANT" or "USER_ALREADY_INVITED"
            or "USER_BLOCKED" or "USER_IS_BLOCKED" or "USER_DEACTIVATED"
            or "USER_NOT_FOUND" or "USER_ID_INVALID" or "INPUT_USER_DEACTIVATED"
            or "USER_IS_BOT" or "USER_RESTRICTED" or "USER_KICKED"
            or "USER_NOT_A_PARTICIPANT" or "PEER_ID_INVALID"
                => new(ErrorTier.Member, ErrorAction.SkipMember, 0,
                    "Member-level: privacy/blocked/already present/bot/deactivated — skip member ONLY, account untouched."),
            // ---- Tier 3: JOB ----
            "CHANNEL_PRIVATE" or "CHAT_ADMIN_REQUIRED" or "CHAT_WRITE_FORBIDDEN"
            or "USER_NOT_PARTICIPANT" or "CHANNEL_INVALID" or "CHAT_FORBIDDEN"
                => new(ErrorTier.Job, ErrorAction.AbortJob, 0,
                    "Fatal job error: private chat / not admin / write forbidden — abort job, do not retry blindly."),
            // ---- Tier 4: TRANSPORT ----
            "CONNECTION_CLOSED" or "TIMEOUT" or "NETWORK_ERROR" or "RPC_CALL_FAIL"
                => new(ErrorTier.Transport, ErrorAction.RetryWithBackoff, 0,
                    "Transient transport: bounded retry with 1/2/4s backoff, participant-reads only for extraction resume."),
            _ => new(ErrorTier.Transport, ErrorAction.RetryWithBackoff, 0,
                "Unknown error: bounded retry, then fail the batch with a full report — never silent, never blind."),
        };
    }
}
