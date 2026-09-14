using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Task E18/F01/US01/T01 (wave w15, NW-05; ADR-010 w15 footer; ADR-022 w15 footer clause 2; ADR-025
/// §I/Rule B1; ADR-009 w15 footer §5c): the one request-scoped seam that collapses the ~19
/// copy-pasted <c>'X-Tenant-Id'</c> parse blocks this wave found across 12 files (74 occurrences
/// across 17 files, measured — see <c>DocumentsEndpointExtensions.cs:556-567</c>'s retired
/// <c>TryResolveTenant</c> for the canonical shape this seam replaces).
///
/// <para>
/// <b><c>X-Tenant-Id</c> is demoted, not deleted.</b> A caller may belong to several workspaces, so
/// the validated token subject alone names no tenant, and only 6 of 36 routes carry
/// <c>{tenantId}</c> in the path. The header becomes an <b>authorized selector</b>: a caller-supplied
/// <em>candidate</em> whose membership is verified against the <b>token subject</b>
/// (<see cref="ICallerIdentity"/>) on every request, before <see cref="ITenantContext.BeginScope"/>
/// — never the other way around ("scope and see what comes back" turns an authorization question
/// into an empty-result question, ADR-009 w15 footer §5c).
/// </para>
///
/// <para>
/// <b>Order, and the status code each step owns:</b>
/// <list type="number">
/// <item>No validated identity at all (<see cref="ICallerIdentity.Resolve"/> returns
/// <see langword="null"/> — no bearer token, or one that failed validation) → <b>401</b>. This is
/// what makes a forged <c>X-Tenant-Id</c> with no token 401 on every route this seam guards
/// (acceptance A15-8), regardless of what the header says.</item>
/// <item>No <c>X-Tenant-Id</c> header, or one that is not a GUID → <b>400</b>. The caller is real;
/// the request is malformed.</item>
/// <item>A well-formed candidate tenant the caller has no live <c>workspace_membership</c> row in →
/// <b>404</b>, never 403 (ADR-025 Rule B1: a 403 here would be a tenant-existence oracle).</item>
/// <item>Otherwise: the tenant scope is entered and handed back open, for the caller to dispose at
/// the end of its own handler — the same lifetime every tenant-scoped handler in this host already
/// manages for its own <c>using var tenantScope = tenantContext.BeginScope(tenantId);</c> call, just
/// relocated here.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why the membership check itself needs a (candidate) scope already open</b>: unlike
/// <c>workspace_user</c>, which also carries the <c>identity_self</c> policy
/// (migration <c>AddWorkspaceUserIdentitySelfReadPolicy</c>) that
/// <c>WorkspaceDirectoryService.ListForIdentityAsync</c> relies on to search *across* tenants with no
/// candidate yet, <c>workspace_membership</c> carries only the ordinary tenant-scoped RLS policy.
/// There is no chicken-and-egg problem here the way there is for <c>GET /api/workspaces</c>, though:
/// this seam already has exactly one candidate (the header), so it tentatively opens that one
/// candidate's own scope, runs the check, and — on success — keeps that same scope open rather than
/// closing it and asking <see cref="ITenantContext.BeginScope"/> to redundantly re-enter the
/// identical tenant a second time. On failure the candidate scope is disposed before the 404 is
/// returned: nothing this seam does ever leaves a stale scope open past its own call.
/// </para>
/// </summary>
internal interface ICallerContext
{
    /// <summary>
    /// Resolves and verifies the caller's tenant for the current request. Check
    /// <see cref="CallerTenantResult.Failure"/> first — when non-null it is the exact <see cref="IResult"/>
    /// the caller's own handler should return unchanged (401/400/404, see the type doc comment).
    /// Otherwise dispose <see cref="CallerTenantResult.Scope"/> when the handler is done, the same
    /// way every existing <c>using var tenantScope = tenantContext.BeginScope(tenantId);</c> call
    /// already is.
    /// </summary>
    Task<CallerTenantResult> ResolveTenantAsync(HttpRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of <see cref="ICallerContext.ResolveTenantAsync"/> — either a verified
/// <see cref="TenantId"/> with its scope already entered (<see cref="Scope"/> non-null,
/// <see cref="Failure"/> null), or a terminal <see cref="IResult"/> the caller's own handler returns
/// as-is (<see cref="Failure"/> non-null, <see cref="TenantId"/>/<see cref="Scope"/> both default).
/// </summary>
internal readonly struct CallerTenantResult
{
    private CallerTenantResult(TenantId tenantId, string? identity, IDisposable? scope, IResult? failure)
    {
        TenantId = tenantId;
        Identity = identity;
        Scope = scope;
        Failure = failure;
    }

    public TenantId TenantId { get; }

    /// <summary>The validated caller identity (the token's <c>oid</c>) the membership was verified
    /// for -- the audit actor and the per-user key every handler used to read off <c>X-User-Id</c>.
    /// Non-null whenever <see cref="Failure"/> is null.</summary>
    public string? Identity { get; }

    public IDisposable? Scope { get; }

    public IResult? Failure { get; }

    public static CallerTenantResult Success(TenantId tenantId, string identity, IDisposable scope) => new(tenantId, identity, scope, failure: null);

    public static CallerTenantResult Fail(IResult failure) => new(default, identity: null, scope: null, failure);
}

internal sealed class CallerContext(
    ICallerIdentity callerIdentity, IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext)
    : ICallerContext
{
    private const string TenantHeaderName = "X-Tenant-Id";

    public async Task<CallerTenantResult> ResolveTenantAsync(
        HttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Step 1 (401): ADR-025 §B — absence of a validated identity is an authentication failure,
        // checked before the tenant header is even looked at. A forged X-Tenant-Id buys nothing
        // without a token (acceptance A15-8).
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return CallerTenantResult.Fail(Results.Unauthorized());
        }

        // Step 2 (400): the caller is real; the request is malformed.
        if (!request.Headers.TryGetValue(TenantHeaderName, out var values)
            || !Guid.TryParse(values.ToString(), out var tenantGuid))
        {
            return CallerTenantResult.Fail(
                Results.BadRequest($"A valid '{TenantHeaderName}' header (a GUID) is required."));
        }

        var tenantId = new TenantId(tenantGuid);

        // Step 3 (404) / Step 4 (scope survives): see the type doc comment for why the candidate
        // scope is opened before the membership check can even run, and why success keeps this
        // exact scope instead of closing and re-entering it.
        var scope = tenantContext.BeginScope(tenantId);
        try
        {
            var isMember = await (
                from user in dbContext.WorkspaceUsers
                join membership in dbContext.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
                where user.TenantId == tenantId
                    && membership.TenantId == tenantId
                    && (user.Email == identity || user.ExternalSubjectId == identity)
                select membership.Id)
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!isMember)
            {
                scope.Dispose();
                return CallerTenantResult.Fail(Results.NotFound());
            }

            return CallerTenantResult.Success(tenantId, identity, scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
