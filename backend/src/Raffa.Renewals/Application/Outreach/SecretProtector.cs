using System.Security.Cryptography;
using System.Text;

namespace Raffa.Renewals.Application.Outreach;

/// <summary>
/// Encrypts the secrets Raffa keeps for a person (mailbox refresh tokens, webhook URLs, push subscriptions)
/// with AES-256-GCM under <see cref="CaseMailOptions.TokenKey"/>, and signs short-lived values (the OAuth
/// state) with HMAC-SHA256 under a key derived from it. Format: <c>v1.</c> + base64url(nonce | tag | cipher).
/// </summary>
public sealed class SecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;
    private readonly byte[] _signKey;

    public SecretProtector(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("The key must be 32 bytes.", nameof(key));
        }

        _key = key;
        _signKey = HMACSHA256.HashData(key, "raffa-sign-v1"u8);
    }

    public static SecretProtector? From(CaseMailOptions options) => options.TryKey(out var key) ? new SecretProtector(key) : null;

    public string Protect(string plain)
    {
        var data = Encoding.UTF8.GetBytes(plain);
        var output = new byte[NonceSize + TagSize + data.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return "v1." + Base64Url(output);
    }

    public string? Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith("v1.", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var input = FromBase64Url(protectedValue[3..]);
            if (input.Length < NonceSize + TagSize)
            {
                return null;
            }

            var plain = new byte[input.Length - NonceSize - TagSize];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary><paramref name="payload"/> + "." + its signature (base64url).</summary>
    public string Sign(string payload)
    {
        var body = Base64Url(Encoding.UTF8.GetBytes(payload));
        return body + "." + Base64Url(HMACSHA256.HashData(_signKey, Encoding.ASCII.GetBytes(body)));
    }

    /// <summary>The payload of a value made by <see cref="Sign"/>, or null when it was altered.</summary>
    public string? Verify(string signed)
    {
        var dot = signed.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return null;
        }

        var body = signed[..dot];
        try
        {
            var expected = HMACSHA256.HashData(_signKey, Encoding.ASCII.GetBytes(body));
            var given = FromBase64Url(signed[(dot + 1)..]);
            return CryptographicOperations.FixedTimeEquals(expected, given) ? Encoding.UTF8.GetString(FromBase64Url(body)) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b += (b.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(b);
    }
}
