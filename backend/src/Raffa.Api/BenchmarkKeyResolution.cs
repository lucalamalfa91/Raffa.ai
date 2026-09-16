using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api;

/// <summary>
/// Task E21/F01/US01/T01 (ADR-002 w17 clause 3, ADR-024 w17 clause 10): the one place in the
/// host that resolves the <b>(supplier name, geography)</b> pair a <c>BenchmarkQuery</c> requires.
/// Both dimensions need cross-module lookups — supplier name through
/// <see cref="ISupplierNameLookup"/> (Suppliers/Products module), geography through
/// <see cref="Raffa.Identity.Workspace.Domain.WorkspaceTenant.Country"/> (Identity/Workspace
/// module) — so resolving them belongs in the host composition layer, not in any single domain
/// module. One resolution per request, two consumers (<c>E21/F02/US01/T01</c> and
/// <c>E21/F03/US01/T01</c>) read the same resolved key rather than re-resolving independently,
/// so two calls to the benchmark service for one screen cannot disagree on which key they used.
///
/// <para>
/// Pattern: <see cref="PortfolioEndpointExtensions.ResolveSupplierNamesAsync"/> — one place per
/// host that calls <see cref="ISupplierNameLookup"/>, wrapped in its own
/// <see cref="ITenantContext.BeginScope"/>. This service is registered Scoped (not Singleton) so
/// it participates in the request's own DI scope and can hold Scoped dependencies
/// (<see cref="IdentityWorkspaceDbContext"/>).
/// </para>
/// </summary>
internal sealed class BenchmarkKeyResolution(
    ISupplierNameLookup supplierNameLookup,
    IdentityWorkspaceDbContext identityDbContext,
    ITenantContext tenantContext)
{
    /// <summary>
    /// Resolves the (supplier name, geography) key for a <c>BenchmarkQuery</c>.
    /// Returns <see cref="BenchmarkKeyResult.Complete"/> when both supplier name and workspace
    /// country are available, or <see cref="BenchmarkKeyResult.Incomplete"/> when either is
    /// missing (no <c>SupplierId</c> on the contract, the id no longer resolves, or the workspace
    /// has no country set). An incomplete key is a <em>result</em>, never a <c>null!</c> —
    /// <c>BenchmarkQuery.Supplier</c>/<c>Geography</c> are non-nullable, so a null-coerced key
    /// would produce a type-check failure at the call site instead of a meaningful abstain entry.
    /// </summary>
    public async Task<BenchmarkKeyResult> ResolveAsync(
        TenantId tenantId,
        EntityId? supplierId,
        CancellationToken cancellationToken = default)
    {
        // Supplier name — the ISupplierNameLookup port requires a scope (same reasoning as
        // PortfolioEndpointExtensions.ResolveSupplierNamesAsync: called outside a scope it returns
        // an empty map, making every contract look supplier-less).
        string? supplierName = null;
        if (supplierId is { } id)
        {
            using var supplierScope = tenantContext.BeginScope(tenantId);
            var names = await supplierNameLookup
                .GetNamesAsync(tenantId, [id], cancellationToken)
                .ConfigureAwait(false);
            names.TryGetValue(id, out supplierName);
        }

        if (supplierName is null)
        {
            return new BenchmarkKeyResult.Incomplete();
        }

        // Geography — WorkspaceTenant.Country (ISO 3166-1 alpha-2). The workspace row's own
        // TenantId equals the tenant id (WorkspaceFactory invariant), so RLS plus the explicit
        // Where predicate both independently narrow to this tenant. The scope must be open for RLS;
        // we open it here rather than assuming one is already active (same rule as AuditQueryService).
        using var workspaceScope = tenantContext.BeginScope(tenantId);
        var country = await identityDbContext.Workspaces
            .Where(w => w.TenantId == tenantId)
            .Select(w => w.Country)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (country is null)
        {
            return new BenchmarkKeyResult.Incomplete();
        }

        return new BenchmarkKeyResult.Complete(supplierName, country);
    }
}

/// <summary>
/// Outcome of <see cref="BenchmarkKeyResolution.ResolveAsync"/> — either a
/// <see cref="Complete"/> key ready for <c>BenchmarkQuery</c>, or an <see cref="Incomplete"/>
/// result that the host maps to a single <c>status: "insufficient_data"</c> entry (ADR-024 w17
/// clause 11: an empty array cannot be told apart from "never wired").
/// </summary>
internal abstract record BenchmarkKeyResult
{
    private BenchmarkKeyResult() { }

    /// <summary>Both supplier name and geography resolved — the caller may build a
    /// <c>BenchmarkQuery</c>.</summary>
    internal sealed record Complete(string Supplier, string Geography) : BenchmarkKeyResult;

    /// <summary>At least one dimension was missing or unresolvable. The caller must not attempt
    /// a benchmark lookup and must emit a single <c>"insufficient_data"</c> entry
    /// (ADR-024 w17 clause 11).</summary>
    internal sealed record Incomplete : BenchmarkKeyResult;
}
