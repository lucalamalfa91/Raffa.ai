using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Application service for task E01/F09/US01/T01 (r0-integration, AC-1 "create workspace" step):
/// persists the <see cref="WorkspaceTenant"/> + default <see cref="WorkspaceRole"/> catalog that
/// <see cref="WorkspaceFactory.CreateWorkspaceWithDefaultRoles"/> only builds in memory. Not
/// previously called by any host — no `/api/workspaces` endpoint existed yet (see
/// <see cref="ServiceCollectionExtensions"/>'s own doc comment and
/// <see cref="WorkspaceMembershipService"/>'s "not yet called by a host" note, which this task
/// resolves for the "create" half; <see cref="WorkspaceMembershipService"/> already covers the
/// "invite" half).
///
/// Opens its own <see cref="ITenantContext.BeginScope"/> for the *new* workspace's own tenant id
/// before inserting — required, not optional: <see cref="WorkspaceTenant"/>'s `Id == TenantId`
/// invariant means the very first row for this tenant is the workspace row itself, and ADR-009's
/// RLS `WITH CHECK` only accepts a write whose `tenant_id` matches the connection's active claim.
/// Mirrors <see cref="WorkspaceMembershipService"/>'s own per-call scoping convention.
///
/// <para>
/// Task E14/F02/US01/T01 (wave w14; ADR-025 §D.2, ADR-009 w14 footer clause 6): the creator of a
/// workspace becomes its Admin <i>by virtue of creating it</i>. <see cref="CreateWorkspaceAsync"/>
/// now also writes the caller's own <see cref="WorkspaceUser"/> and Admin
/// <see cref="WorkspaceMembership"/> row, reusing
/// <see cref="WorkspaceMembershipFactory.CreateInvitedUser"/> and
/// <see cref="WorkspaceMembershipFactory.CreateMembership"/> rather than new construction logic —
/// in the <i>same</i> single <see cref="ITenantContext.BeginScope"/> and one
/// <c>SaveChangesAsync</c> this method already used for the workspace and its role catalog, so a
/// partial bootstrap is never
/// reachable — not even by a failure path (ADR-009 w14 footer clause 6: "strictly
/// one-scope-per-request"; the scope is entered only after every validation below has already
/// succeeded).
/// </para>
/// </summary>
public sealed class WorkspaceProvisioningService(
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock)
{
    /// <summary>
    /// The total map NW-24 fixes (ADR-003 w14 footer clause 1; design export's country list,
    /// <c>markup.html:56</c>): ISO 3166-1 alpha-2 country to ISO 4217 alpha-3 currency. Total over
    /// the closed list it covers — there is no "valid country, unknown currency" state and
    /// therefore no fallback branch; a <paramref name="country"/> outside this set is simply
    /// invalid (see <see cref="CreateWorkspaceAsync"/>'s own validation).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CurrencyByCountry =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CH"] = "CHF",
            ["IT"] = "EUR",
            ["DE"] = "EUR",
            ["AT"] = "EUR",
        };

    /// <summary>Mirrors <see cref="WorkspaceTenant.Industry"/>'s own <c>HasMaxLength(120)</c>
    /// (<c>WorkspaceTenantConfiguration.cs:25</c>) — validated here so an over-long value is this
    /// task's own clean <see cref="Result{T}"/> 400, never a raw Postgres length error, the same
    /// discipline <see cref="WorkspaceMembershipFactory.CreateInvitedUser"/> already applies to an
    /// over-long email.</summary>
    private const int MaxIndustryLength = 120;

    /// <summary>
    /// Creates a new workspace named <paramref name="name"/> together with its full default role
    /// catalog (Admin/Procurement/Legal/Finance/Read-only) and makes <paramref name="callerIdentity"/>
    /// its Admin (ADR-025 Rule D.2b) — four writes, one scope, one <c>SaveChangesAsync</c>. Fails
    /// cleanly instead of surfacing a raw EF/Postgres constraint error when <paramref name="name"/>
    /// is blank, <paramref name="callerIdentity"/> is blank, <paramref name="callerIdentity"/>
    /// does not parse as the email <see cref="WorkspaceMembershipFactory.CreateInvitedUser"/>
    /// expects (ADR-025 §B: "identity presented but malformed" stays a 400 through this existing
    /// <see cref="Result{T}"/> failure path, exactly like a blank name always has), <paramref
    /// name="industry"/> is over-long, or <paramref name="country"/> is present but not one of the
    /// closed NW-24 list. Every failure below returns before <see cref="ITenantContext.BeginScope"/>
    /// is ever entered, so none of them can leave a partial tenant behind.
    ///
    /// <para>
    /// Task E14/F03/US01/T01 (wave w14, NW-24; ADR-003 w14 footer clause 1): <paramref
    /// name="industry"/> and <paramref name="country"/> are both optional free-form profile fields —
    /// a blank/whitespace-only value is stored and later returned as <see langword="null"/>
    /// (absent), never as an empty string. <paramref name="country"/> additionally derives and
    /// stores <see cref="WorkspaceTenant.Currency"/> via <see cref="CurrencyByCountry"/> — the
    /// currency is never itself an input, so there is no "invalid currency" case to validate. A
    /// workspace's stored currency is a display default only; no code path here or anywhere else
    /// lets it override a contract's own extracted currency (ADR-003 w14 footer clause 1).
    /// </para>
    /// </summary>
    public async Task<Result<WorkspaceTenant>> CreateWorkspaceAsync(
        string name,
        string callerIdentity,
        string? industry = null,
        string? country = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            return Result<WorkspaceTenant>.Failure("A workspace 'name' is required.");
        }

        var trimmedIdentity = callerIdentity?.Trim() ?? string.Empty;
        if (trimmedIdentity.Length == 0)
        {
            return Result<WorkspaceTenant>.Failure("A caller identity is required to create a workspace.");
        }

        string? normalizedIndustry = null;
        if (!string.IsNullOrWhiteSpace(industry))
        {
            var trimmedIndustry = industry.Trim();
            if (trimmedIndustry.Length > MaxIndustryLength)
            {
                return Result<WorkspaceTenant>.Failure(
                    $"'industry' must be at most {MaxIndustryLength} characters.");
            }

            normalizedIndustry = trimmedIndustry;
        }

        string? normalizedCountry = null;
        string? derivedCurrency = null;
        if (!string.IsNullOrWhiteSpace(country))
        {
            var candidateCountry = country.Trim().ToUpperInvariant();
            if (!CurrencyByCountry.TryGetValue(candidateCountry, out derivedCurrency))
            {
                var supportedCountries = string.Join(", ", CurrencyByCountry.Keys.OrderBy(c => c, StringComparer.Ordinal));
                return Result<WorkspaceTenant>.Failure(
                    $"'{country}' is not a supported workspace country. Expected one of: {supportedCountries}.");
            }

            normalizedCountry = candidateCountry;
        }

        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(trimmedName, clock);
        workspace.Industry = normalizedIndustry;
        workspace.Country = normalizedCountry;
        workspace.Currency = derivedCurrency;

        var now = workspace.CreatedAt;
        var adminRole = roles.Single(r => r.Name == WorkspaceRoleName.Admin);

        var creatorResult = WorkspaceMembershipFactory.CreateInvitedUser(workspace.TenantId, trimmedIdentity, now);
        if (creatorResult.IsFailure)
        {
            return Result<WorkspaceTenant>.Failure(creatorResult.Error);
        }

        var membershipResult = WorkspaceMembershipFactory.CreateMembership(creatorResult.Value, adminRole, now);
        if (membershipResult.IsFailure)
        {
            return Result<WorkspaceTenant>.Failure(membershipResult.Error);
        }

        using var _ = tenantContext.BeginScope(workspace.TenantId);

        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        db.WorkspaceUsers.Add(creatorResult.Value);
        db.WorkspaceMemberships.Add(membershipResult.Value);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<WorkspaceTenant>.Success(workspace);
    }
}
