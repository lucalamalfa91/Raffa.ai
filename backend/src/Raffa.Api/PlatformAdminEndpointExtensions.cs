using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Infrastructure;

namespace Raffa.Api;

/// <summary>
/// The portal admin surface for Raffa staff (<see cref="PlatformAdminOptions"/>): prospect invitations,
/// the workspace registry (metadata only) and suspension. A platform admin is not a member of any
/// workspace and nothing here reads tenant content: it works only on the platform tables.
///
/// <para>Guard on every <c>/api/admin/*</c> route: 401 without an identity; <b>404</b> (never 403) for
/// anyone who is not a platform admin, so the surface is not discoverable. The one route outside that
/// guard is <c>POST /api/platform/invitations/accept</c>, which any signed-in prospect calls with the
/// secret from their link.</para>
/// </summary>
public static class PlatformAdminEndpointExtensions
{
    public const string InvitationTokenHeader = "X-Platform-Invitation-Token";

    public static IEndpointRouteBuilder MapPlatformAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/me", GetStatus);
        endpoints.MapGet("/api/admin/overview", Guarded((svc, _, ct) => OverviewAsync(svc, ct)));
        endpoints.MapGet("/api/admin/workspaces", Guarded((svc, _, ct) => ListWorkspacesAsync(svc, ct)));
        endpoints.MapPost("/api/admin/workspaces/{tenantId:guid}/suspend", (Guid tenantId, HttpContext http, ICallerIdentity id, PlatformAdminGuard guard, PlatformAdminService svc, CancellationToken ct) =>
            RunAsync(id, guard, () => SetStatusAsync(svc, tenantId, PlatformAdminService.Suspended, ct)));
        endpoints.MapPost("/api/admin/workspaces/{tenantId:guid}/reactivate", (Guid tenantId, HttpContext http, ICallerIdentity id, PlatformAdminGuard guard, PlatformAdminService svc, CancellationToken ct) =>
            RunAsync(id, guard, () => SetStatusAsync(svc, tenantId, PlatformAdminService.Active, ct)));
        endpoints.MapGet("/api/admin/invitations", Guarded((svc, _, ct) => ListInvitationsAsync(svc, ct)));
        endpoints.MapPost("/api/admin/invitations", (CreatePlatformInvitationRequest body, ICallerIdentity id, PlatformAdminGuard guard, PlatformAdminService svc, InvitationOptions invitationOptions, CancellationToken ct) =>
            RunAsync(id, guard, () => CreateInvitationAsync(svc, invitationOptions, body.Email, id.Resolve()!, ct)));
        endpoints.MapPost("/api/admin/invitations/{invitationId:guid}/regenerate", (Guid invitationId, ICallerIdentity id, PlatformAdminGuard guard, PlatformAdminService svc, InvitationOptions invitationOptions, CancellationToken ct) =>
            RunAsync(id, guard, () => RegenerateAsync(svc, invitationOptions, invitationId, id.Resolve()!, ct)));
        endpoints.MapDelete("/api/admin/invitations/{invitationId:guid}", (Guid invitationId, ICallerIdentity id, PlatformAdminGuard guard, PlatformAdminService svc, CancellationToken ct) =>
            RunAsync(id, guard, async () => await svc.RevokeInvitationAsync(invitationId, ct).ConfigureAwait(false)
                ? Results.NoContent()
                : Results.NotFound()));
        endpoints.MapGet("/api/admin/users", Guarded((svc, _, ct) => ListUsersAsync(svc, ct)));
        endpoints.MapGet("/api/admin/market-network", (ICallerIdentity id, PlatformAdminGuard guard, IServiceProvider services, CancellationToken ct) =>
            RunAsync(id, guard, () => MarketNetworkAsync(services, ct)));
        endpoints.MapPost("/api/platform/invitations/accept", AcceptAsync);
        return endpoints;
    }

    private static IResult GetStatus(ICallerIdentity callerIdentity, PlatformAdminGuard guard) =>
        callerIdentity.Resolve() is null
            ? Results.Unauthorized()
            : Results.Ok(new { isPlatformAdmin = guard.IsPlatformAdmin() });

    /// <summary>Runs <paramref name="action"/> only for a platform admin: 401 anonymous, 404 otherwise.</summary>
    private static async Task<IResult> RunAsync(ICallerIdentity callerIdentity, PlatformAdminGuard guard, Func<Task<IResult>> action)
    {
        if (callerIdentity.Resolve() is null)
        {
            return Results.Unauthorized();
        }

        return guard.IsPlatformAdmin() ? await action().ConfigureAwait(false) : Results.NotFound();
    }

    private static Func<ICallerIdentity, PlatformAdminGuard, PlatformAdminService, CancellationToken, Task<IResult>> Guarded(
        Func<PlatformAdminService, ICallerIdentity, CancellationToken, Task<IResult>> action) =>
        (id, guard, svc, ct) => RunAsync(id, guard, () => action(svc, id, ct));

    /// <summary>
    /// Monitors the invisible market-network flow without ever showing a customer's data: how many anonymised
    /// observations and workspaces the network holds, the aggregated deals it publishes into the shared market
    /// base (P25/P50/P75 only), and the latest refresh runs from the system audit trail. The contributing
    /// workspace sees its own "market.observation.recorded" rows in its audit as usual.
    /// </summary>
    private static async Task<IResult> MarketNetworkAsync(IServiceProvider services, CancellationToken ct)
    {
        var network = services.GetService<Raffa.Market.Network.MarketNetworkService>();
        var options = services.GetService<Raffa.Market.Network.MarketObservationOptions>();
        if (network is null || options is null)
        {
            return Results.Ok(new { available = false });
        }

        var snapshot = await network.BuildAsync(ct).ConfigureAwait(false);
        var audit = services.GetService<Raffa.Audit.Infrastructure.IAuditQueryService>();
        var refreshes = audit is null
            ? []
            : (await audit.GetEventsAsync(Raffa.Market.Ingestion.MarketIngestionService.SystemTenantId, ct).ConfigureAwait(false))
                .Where(e => e.Action == "market.network.refreshed")
                .Take(10)
                .Select(e => new { at = e.OccurredAt, detail = e.Detail })
                .ToList<object>();
        return Results.Ok(new
        {
            available = true,
            enabled = options.Enabled,
            minWorkspaces = options.MinWorkspaces,
            refreshHours = options.RefreshHours,
            observations = snapshot.Observations,
            workspaces = snapshot.Workspaces,
            suppliersPriced = snapshot.SuppliersPriced,
            deals = snapshot.Deals.Select(d => new
            {
                supplier = d.Supplier, product = d.Product, termMonths = d.TermMonths, currency = d.Currency, workspaces = d.SampleSize,
                p25 = d.UnitPriceP25, p50 = d.UnitPriceP50, p75 = d.UnitPriceP75,
                discountPct = d.DiscountAchievedPct, upliftCapPct = d.UpliftCapPct, noticeDays = d.NoticeDays, paymentTerms = d.PaymentTerms,
            }),
            refreshes,
        });
    }

    private static async Task<IResult> OverviewAsync(PlatformAdminService svc, CancellationToken ct) =>
        Results.Ok(await svc.GetOverviewAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> ListWorkspacesAsync(PlatformAdminService svc, CancellationToken ct)
    {
        var rows = await svc.ListWorkspacesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            workspaces = rows.Select(r => new
            {
                id = r.TenantId, name = r.Name, adminEmail = r.AdminEmail, createdAt = r.CreatedAt, status = r.Status,
            }),
        });
    }

    private static async Task<IResult> SetStatusAsync(PlatformAdminService svc, Guid tenantId, string status, CancellationToken ct) =>
        await svc.SetWorkspaceStatusAsync(tenantId, status, ct).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> ListInvitationsAsync(PlatformAdminService svc, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await svc.ListInvitationsAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            invitations = rows.Select(r => new
            {
                id = r.Id, email = r.Email, status = PlatformAdminService.StatusOf(r, now),
                createdAt = r.CreatedAt, expiresAt = r.ExpiresAt, acceptedAt = r.AcceptedAt, workspaceId = r.WorkspaceId,
            }),
        });
    }

    private static async Task<IResult> ListUsersAsync(PlatformAdminService svc, CancellationToken ct)
    {
        var rows = await svc.ListInvitationsAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            users = rows.Where(r => r.AcceptedAt is not null).Select(r => new
            {
                email = r.Email, joinedAt = r.AcceptedAt, workspaceId = r.WorkspaceId, hasWorkspace = r.WorkspaceId is not null,
            }),
        });
    }

    private static async Task<IResult> CreateInvitationAsync(
        PlatformAdminService svc, InvitationOptions invitationOptions, string? email, string createdBy, CancellationToken ct)
    {
        if (!LooksLikeEmail(email))
        {
            return Results.BadRequest("A valid 'email' is required.");
        }

        return Issued(await svc.IssueInvitationAsync(email!, createdBy, ct).ConfigureAwait(false), invitationOptions);
    }

    private static async Task<IResult> RegenerateAsync(
        PlatformAdminService svc, InvitationOptions invitationOptions, Guid invitationId, string createdBy, CancellationToken ct)
    {
        var existing = await svc.GetInvitationAsync(invitationId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.AcceptedAt is not null)
        {
            return Results.Conflict("This invitation was already accepted.");
        }

        return Issued(await svc.IssueInvitationAsync(existing.Email, createdBy, ct).ConfigureAwait(false), invitationOptions);
    }

    private static IResult Issued(PlatformInvitationIssued issued, InvitationOptions invitationOptions)
    {
        // Same absolute-base convention as workspace invitations; the secret rides in the fragment,
        // never a path or query string, so it is not sent to any server or written to an access log.
        var baseUrl = (invitationOptions.AcceptUrlBase ?? string.Empty).TrimEnd('/');
        return Results.Created($"/api/admin/invitations/{issued.Id}", new
        {
            id = issued.Id, email = issued.Email, expiresAt = issued.ExpiresAt,
            acceptUrl = $"{baseUrl}/prospect/accept#{issued.Token}",
        });
    }

    /// <summary>`POST /api/platform/invitations/accept` with the secret in <see cref="InvitationTokenHeader"/>.
    /// 200 accepted; 404 unknown; 410 expired or revoked; 409 already used by someone else; 403 wrong account.</summary>
    private static async Task<IResult> AcceptAsync(
        HttpRequest request, ICallerIdentity callerIdentity, PlatformAdminService svc, CancellationToken ct)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var token = request.Headers[InvitationTokenHeader].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.BadRequest($"The '{InvitationTokenHeader}' header is required.");
        }

        var status = await svc.AcceptInvitationAsync(token, identity, callerIdentity.ResolveEmail(), ct).ConfigureAwait(false);
        return status switch
        {
            PlatformInvitationAcceptStatus.Accepted => Results.Ok(new { accepted = true }),
            PlatformInvitationAcceptStatus.Expired or PlatformInvitationAcceptStatus.Revoked => Results.StatusCode(StatusCodes.Status410Gone),
            PlatformInvitationAcceptStatus.AlreadyUsed => Results.Conflict("This invitation has already been used."),
            PlatformInvitationAcceptStatus.WrongAccount => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => Results.NotFound(),
        };
    }

    private static bool LooksLikeEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 320 && value.Contains('@') && !value.Contains(' ');
}
