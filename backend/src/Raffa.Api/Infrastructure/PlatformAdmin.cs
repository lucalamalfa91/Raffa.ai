using Microsoft.Extensions.Options;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Configuration section <c>PlatformAdmin</c>: the Raffa staff identities that may use the portal
/// admin surface (<c>/api/admin/*</c>). A platform admin is a person, not a tenant role: it sits above
/// every workspace and is deliberately <b>not</b> a row in any tenant-scoped table, so nothing here
/// widens RLS (ADR-009) or adds a bypass. Set in deployed environments with
/// <c>PlatformAdmin__ObjectIds__0=&lt;Entra oid&gt;</c>, <c>PlatformAdmin__ObjectIds__1=...</c>.
/// Empty (the default) means nobody is a platform admin and every admin route answers 404.
/// </summary>
public sealed class PlatformAdminOptions
{
    public const string SectionName = "PlatformAdmin";

    /// <summary>Entra object ids (the validated token's <c>oid</c>, the same value
    /// <see cref="ICallerIdentity.Resolve"/> returns). Matched ordinally, like every other
    /// <c>ExternalSubjectId</c> comparison in the product.</summary>
    public string[] ObjectIds { get; set; } = [];
}

/// <summary>"Is the caller a Raffa platform admin?" — one allowlist lookup, no database.</summary>
internal sealed class PlatformAdminGuard(ICallerIdentity callerIdentity, IOptions<PlatformAdminOptions> options)
{
    public bool IsPlatformAdmin()
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return false;
        }

        return options.Value.ObjectIds.Any(
            configured => !string.IsNullOrWhiteSpace(configured)
                && string.Equals(configured.Trim(), identity, StringComparison.Ordinal));
    }
}
