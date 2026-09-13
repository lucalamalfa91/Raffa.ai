namespace Raffa.SharedKernel.Tenancy;

/// <summary>
/// Ambient accessor for the tenant owning the request or worker job currently executing
/// (ADR-009). Set once per request/job via <see cref="BeginScope"/>; the data-access layer's
/// connection interceptor (<see cref="TenantRlsConnectionInterceptor"/>) reads
/// <see cref="Current"/> to establish the per-connection `app.tenant_id` claim that Postgres
/// Row-Level Security enforces against.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The tenant for the code currently executing, or <see langword="null"/> when no tenant
    /// scope is active (for example a health check, or code that has not entered a scope yet).
    /// <see langword="null"/> means the RLS claim is left unset on the connection, so RLS denies
    /// every tenant-scoped row — fail closed, never fail open.
    /// </summary>
    TenantId? Current { get; }

    /// <summary>
    /// Enters a tenant scope for the remainder of the current async call chain. Dispose the
    /// returned handle when the request/job completes to restore the previous value. ADR-009
    /// expects exactly one scope per request/worker job; nested scopes are supported (the
    /// previous value is restored on dispose) but are not the expected usage.
    /// </summary>
    IDisposable BeginScope(TenantId tenantId);
}

/// <summary>
/// Ambient accessor for the identity of the caller executing the current request/worker job
/// (ADR-009 w14 footer clause 1, ADR-025 §A/§F.2). Deliberately a sibling of
/// <see cref="ITenantContext"/>, not a member of it: tenant answers "which tenant's rows may this
/// connection see" (<see cref="ITenantContext.Current"/>), identity answers "who is calling" --
/// and <c>GET /api/workspaces</c> (NW-01) needs the second with the first deliberately absent, to
/// discover which tenants the caller belongs to before any tenant scope exists. Either context may
/// be active without the other. The data-access layer's connection interceptor
/// (<see cref="TenantRlsConnectionInterceptor"/>) reads <see cref="Current"/> to establish the
/// per-connection `app.identity_subject` claim that the `identity_self` Postgres Row-Level
/// Security policy (`identity-workspace.sql`) enforces against.
/// </summary>
public interface ICallerIdentityContext
{
    /// <summary>
    /// The caller identity for the code currently executing, already trimmed and lower-cased (the
    /// `identity_self` policy compares `lower(email)`), or <see langword="null"/> when no identity
    /// scope is active. <see langword="null"/> means the `app.identity_subject` GUC is left unset
    /// on the connection -- fail closed, never an empty string standing in for absence.
    /// </summary>
    string? Current { get; }

    /// <summary>
    /// Enters an identity scope for the remainder of the current async call chain. Dispose the
    /// returned handle when the request/job completes to restore the previous value. Independent
    /// of <see cref="ITenantContext.BeginScope"/> -- entering one does not require, and has no
    /// effect on, the other.
    /// </summary>
    IDisposable BeginIdentityScope(string identity);
}
