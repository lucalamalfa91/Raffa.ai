using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Raffa.AiGateway.Logging;

namespace Raffa.AiGateway.Tests.Logging;

/// <summary>F3-T11: the audit query hash is keyed when a key is configured and unchanged when it is not.</summary>
public sealed class KeyedQueryHasherTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<LogLevel> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(logLevel);
    }

    [Fact]
    public void Without_a_key_the_hash_is_todays_unkeyed_sha256()
    {
        var hasher = new KeyedQueryHasher(() => null);

        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("salesforce price increase")));

        Assert.Equal(expected, hasher.Compute("salesforce price increase"));
        Assert.Equal(expected, KeyedQueryHasher.Unkeyed("salesforce price increase"));
    }

    [Fact]
    public void A_blank_key_counts_as_absent()
    {
        Assert.Equal(KeyedQueryHasher.Unkeyed("q"), new KeyedQueryHasher(() => "  ").Compute("q"));
    }

    [Fact]
    public void With_a_key_the_hash_is_hmac_sha256_and_depends_on_the_key()
    {
        var one = new KeyedQueryHasher(() => "key-one").Compute("q");
        var two = new KeyedQueryHasher(() => "key-two").Compute("q");

        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("key-one"), Encoding.UTF8.GetBytes("q")));
        Assert.Equal(expected, one);
        Assert.NotEqual(two, one);
        Assert.NotEqual(KeyedQueryHasher.Unkeyed("q"), one);
        Assert.Equal(64, one.Length);
    }

    [Fact]
    public void The_missing_key_is_logged_once_and_a_rotated_key_applies_without_a_restart()
    {
        string? key = null;
        var logger = new CapturingLogger();
        var hasher = new KeyedQueryHasher(() => key, logger);

        var plain = hasher.Compute("a");
        hasher.Compute("b");
        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0]);

        key = "rotated";
        Assert.NotEqual(plain, hasher.Compute("a"));
        Assert.Single(logger.Entries);
    }
}
