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
