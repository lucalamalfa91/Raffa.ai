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
        // Normalised once, here, so every reader of Current (today: only
        // TenantRlsConnectionInterceptor) sees exactly the value the `identity_self` policy's own
        // `lower(email)` comparison expects (ADR-025 §F.1) -- matching
        // WorkspaceMembershipFactory.CreateInvitedUser's existing trim of the same identity class.
        Ambient.Value = identity.Trim().ToLowerInvariant();
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
