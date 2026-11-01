using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Raffa.SharedKernel.Fixtures;

/// <summary>A member of the fixture tenant. <see cref="Key"/> is what the
/// <c>X-Raffa-Fixture-User</c> header carries (<c>luca</c> | <c>marta</c>).</summary>
public sealed record FixtureUser(
    string Key,
    string DisplayName,
    string Title,
    string Email,
    string ExternalSubjectId,
    string Role,
    bool IsApprover);

/// <summary>Everything a seeder needs: the tenant, the users, the scenario clock and the fixture
/// JSON directory (<c>backend/fixtures/askraffa4</c>).</summary>
public sealed class FixtureSeedContext(
    TenantId tenantId,
    string tenantName,
    IReadOnlyList<FixtureUser> users,
    DateTimeOffset now,
    string dataDirectory)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TenantId TenantId { get; } = tenantId;

    public string TenantName { get; } = tenantName;

    public IReadOnlyList<FixtureUser> Users { get; } = users;

    /// <summary>The scenario clock at the time of the reset (<c>Fixtures:Now</c>, default 2026-09-08).</summary>
    public DateTimeOffset Now { get; } = now;

    public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public string DataDirectory { get; } = dataDirectory;

    public FixtureUser User(string key) =>
        Users.FirstOrDefault(u => string.Equals(u.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown fixture user '{key}'.", nameof(key));

    /// <summary>Reads <c>&lt;DataDirectory&gt;/&lt;fileName&gt;</c> as camelCase JSON.</summary>
    public T ReadJson<T>(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Json)
            ?? throw new InvalidOperationException($"Fixture file '{path}' is empty.");
    }

    /// <summary>A stable id for a fixture entity: the same tenant, kind and key always give the same
    /// uuid (<see cref="FixtureIds.Deterministic"/>), so links survive a reset.</summary>
    public EntityId Id(string kind, object key) => new(FixtureIds.Deterministic(TenantId.Value, $"{kind}:{key}"));
}

/// <summary>Deterministic ids (RFC 4122 version 5, SHA-1 name-based).</summary>
public static class FixtureIds
{
    public static Guid Deterministic(Guid @namespace, string name)
    {
        var nsBytes = @namespace.ToByteArray(bigEndian: true);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        nsBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, nsBytes.Length);
#pragma warning disable CA5350 // SHA-1 is what UUIDv5 is defined with; not a security use.
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>The id of fixture contract <paramref name="fixtureId"/> (1 Salesforce, 2 Microsoft,
    /// 3 AWS, 4 DocuSign) in <paramref name="tenantId"/>.</summary>
    public static EntityId Contract(TenantId tenantId, int fixtureId) =>
        new(Deterministic(tenantId.Value, $"contract:{fixtureId}"));
}
