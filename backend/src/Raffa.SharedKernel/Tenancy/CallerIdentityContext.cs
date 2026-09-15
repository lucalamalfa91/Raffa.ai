namespace Raffa.SharedKernel.Tenancy;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="ICallerIdentityContext"/>. Same
/// ambient-per-async-flow shape as <see cref="TenantContext"/> (see its doc comment for why this
/// is safe to register as a DI singleton): the backing store is static, and each request or job's
/// <see cref="AsyncLocal{T}"/> value flows independently with its own async call chain, so
/// concurrent requests/jobs never see each other's caller identity. Independent of
/// <see cref="TenantContext"/>'s own ambient store -- entering one has no effect on the other.
/// </summary>
public sealed class CallerIdentityContext : ICallerIdentityContext
{
    private static readonly AsyncLocal<string?> Ambient = new();

    public string? Current => Ambient.Value;

    public IDisposable BeginIdentityScope(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var previousValue = Ambient.Value;
        // Trimmed only -- never lower-cased (ADR-010 w16 footer S16-2; task E18/F02/US01/T01,
        // correcting this method's own pre-w16 shape). This value becomes the `app.identity_subject`
        // GUC the `identity_self` policy reads (`identity-workspace.sql:198-206`), whose two legs
        // already normalize themselves rather than sharing one case fold: `lower(email) =
        // lower(guc)` lower-cases both sides in SQL, so pre-lowering here bought that leg nothing,
        // while `external_subject_id = guc` is deliberately ordinal -- `oid` is an opaque,
        // case-sensitive Entra object id (`Raffa.Api.Infrastructure.CallerIdentity.cs:60-68`).
        // Force-lowering here silently stopped that leg matching any row whose `ExternalSubjectId`
        // was not itself a canonical lowercase GUID -- benign only while every subject happens to be
        // one, and nothing enforces that (the column is `character varying(200)`). Only whitespace
        // is trimmed, still matching WorkspaceMembershipFactory.CreateInvitedUser's own trim of the
        // same identity class.
        Ambient.Value = identity.Trim();
        return new ScopePopper(previousValue);
    }

    private sealed class ScopePopper(string? previousValue) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = previousValue;
        }
    }
}
