using System.Text.RegularExpressions;

namespace Infinitygram.Application;

/// <summary>
/// Last line of defence for logs, reports and audit details.
/// api_hash, bot tokens, master keys, OTPs, passwords and full phone numbers
/// must NEVER reach a log, a report, an audit entry or an exception message.
/// Callers pass every human-readable detail through Redact() first.
/// This does not replace vault encryption — it prevents accidental leaks
/// from the code paths that are allowed to see a value in memory.
/// </summary>
public static partial class SecretRedactor
{
    [GeneratedRegex(@"\+?\d{10,}", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    public static string Redact(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var result = text;
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret) && secret.Length >= 4)
                result = result.Replace(secret, "***", StringComparison.Ordinal);
        }
        // Mask any long digit run (phone-like) to its last 2 digits.
        result = PhoneRegex().Replace(result, m =>
            m.Value.Length <= 4 ? "***" : "***" + m.Value[^2..]);
        return result;
    }

    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "***";
        return "***" + phone[^2..];
    }
}
