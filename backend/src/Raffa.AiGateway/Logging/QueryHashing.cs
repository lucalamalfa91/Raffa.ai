using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Raffa.AiGateway.Logging;

/// <summary>
/// F3-T11 (F3-D10): the hash an audit row carries instead of a web query. Unkeyed SHA-256 of a short,
/// guessable text can be reversed by a dictionary; a keyed hash (HMAC-SHA256) cannot be without the key.
/// Every site that writes a <c>queryHash=</c> must use the same instance, so the rows still correlate.
/// </summary>
public interface IQueryHasher
{
    /// <summary>Upper-case hex digest of <paramref name="input"/> (64 characters either way).</summary>
    string Compute(string input);
}

/// <summary>
/// <see cref="IQueryHasher"/> over <c>AiGateway:QueryHash:Key</c>, read on every call so a rotated key
/// applies without a restart. Key absent or blank: the unkeyed SHA-256 the platform always used (no
/// behaviour change), and one warning is logged the first time so the gap is visible.
/// </summary>
public sealed class KeyedQueryHasher : IQueryHasher
{
    public const string KeyConfigurationPath = "AiGateway:QueryHash:Key";

    private readonly Func<string?> _key;
    private readonly ILogger? _logger;
    private int _warned;

    public KeyedQueryHasher(IConfiguration configuration, ILogger<KeyedQueryHasher>? logger = null)
        : this(() => configuration[KeyConfigurationPath], logger)
    {
    }

    public KeyedQueryHasher(Func<string?> key, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
        _logger = logger;
    }

    public string Compute(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var data = Encoding.UTF8.GetBytes(input);
        var key = _key();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                _logger?.LogWarning(
                    "{KeyPath} is not configured: query hashes in audit rows are unkeyed SHA-256 and can be reversed by dictionary. Configure a secret key.",
                    KeyConfigurationPath);
            }

            return Convert.ToHexString(SHA256.HashData(data));
        }

        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), data));
    }

    /// <summary>The unkeyed digest: what a site without a hasher computes.</summary>
    public static string Unkeyed(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
