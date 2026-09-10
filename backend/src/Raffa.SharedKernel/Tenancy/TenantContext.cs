namespace Raffa.SharedKernel.Tenancy;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="ITenantContext"/>. The
/// backing store is static (ADR-009's ambient-per-request/job model), so this type is safe to
/// register as a DI singleton: each request or job's <see cref="AsyncLocal{T}"/> value flows
/// independently with its own async call chain — the same mechanism ASP.NET Core's
/// `IHttpContextAccessor` uses — so concurrent requests/jobs never see each other's tenant.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private static readonly AsyncLocal<TenantId?> Ambient = new();

    public TenantId? Current => Ambient.Value;

    public IDisposable BeginScope(TenantId tenantId)
    {
        var previousValue = Ambient.Value;
        Ambient.Value = tenantId;
        return new ScopePopper(previousValue);
    }

    private sealed class ScopePopper(TenantId? previousValue) : IDisposable
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
