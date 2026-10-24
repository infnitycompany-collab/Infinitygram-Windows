using System.Security.Cryptography;
using System.Text;
using Infinitygram.Application;

namespace Infinitygram.Infrastructure;

/// <summary>
/// IGV2 vault: AES-256-GCM, key = PBKDF2-SHA256 (600,000 iterations) then HKDF-SHA256.
/// Wire format: [IGV2 magic 4B][salt 16B][nonce 12B][tag 16B][ciphertext].
/// Any tampering fails authentication — Decrypt throws, never returns bad plaintext.
/// Every Encrypt/Decrypt call is audited by type only (never the secret itself).
/// </summary>
public sealed class VaultService
{
    private static readonly byte[] Magic = "IGV2"u8.ToArray();
    private readonly IAuditStore _audit;

    public VaultService(IAuditStore audit) => _audit = audit;

    public byte[] Encrypt(string masterKey, string plaintext, string accountId)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterKey);
        ArgumentNullException.ThrowIfNull(plaintext);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(masterKey, salt);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plain, cipher, tag);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plain);
        var result = new byte[Magic.Length + salt.Length + nonce.Length + tag.Length + cipher.Length];
        var o = 0;
        Buffer.BlockCopy(Magic, 0, result, o, Magic.Length); o += Magic.Length;
        Buffer.BlockCopy(salt, 0, result, o, salt.Length); o += salt.Length;
        Buffer.BlockCopy(nonce, 0, result, o, nonce.Length); o += nonce.Length;
        Buffer.BlockCopy(tag, 0, result, o, tag.Length); o += tag.Length;
        Buffer.BlockCopy(cipher, 0, result, o, cipher.Length);
        _audit.Append(new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow,
            accountId, "vault.encrypt", "vault", "ok"));
        return result;
    }

    public string Decrypt(string masterKey, byte[] sealedData, string accountId)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterKey);
        ArgumentNullException.ThrowIfNull(sealedData);
        var min = Magic.Length + 16 + 12 + 16;
        if (sealedData.Length < min || !sealedData.AsSpan(0, 4).SequenceEqual(Magic))
            throw new CryptographicException("Invalid vault payload.");
        var salt = sealedData.AsSpan(4, 16).ToArray();
        var nonce = sealedData.AsSpan(20, 12).ToArray();
        var tag = sealedData.AsSpan(32, 16).ToArray();
        var cipher = sealedData.AsSpan(48).ToArray();
        var key = DeriveKey(masterKey, salt);
        var plain = new byte[cipher.Length];
        try
        {
            using (var aes = new AesGcm(key, 16))
                aes.Decrypt(nonce, cipher, tag, plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var text = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        _audit.Append(new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow,
            accountId, "vault.decrypt", "vault", "ok"));
        return text;
    }

    private static byte[] DeriveKey(string masterKey, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(masterKey, salt, 600_000, HashAlgorithmName.SHA256);
        var prk = pbkdf2.GetBytes(32);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, prk, 32,
            salt, Encoding.UTF8.GetBytes("infinitygram-vault-igv2"));
        CryptographicOperations.ZeroMemory(prk);
        return key;
    }
}
