using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Quotes.Application.AssessmentV4;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// AskRaffa 4.0 Offerta (README §8; BACKEND-SPEC §2.11, §4 Quotes): the quote read for the three
/// steps (<c>GET /api/quotes/{id}/v4</c>), the extraction confirmation, the decision and the
/// counter-proposal hand-off. The comparison is never against a market benchmark; the older
/// <c>/assessment</c> endpoints stay for the pre-4.0 surface, which 4.0 does not call. The 4.0 list
/// (<c>GET /api/quotes?status=…</c>) is answered by <see cref="ListAsync"/> from
/// <see cref="QuotesEndpointExtensions"/>.
/// </summary>
public static class QuoteOffersEndpointExtensions
{
    public static IEndpointRouteBuilder MapQuoteOffersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/quotes/{id}/v4", GetAsync);
        endpoints.MapPost("/api/quotes/{id}/confirm-extraction", ConfirmAsync);
        endpoints.MapPost("/api/quotes/{id}/decision", DecideAsync);
        endpoints.MapPost("/api/quotes/{id}/counter-proposal", CounterProposalAsync);
        return endpoints;
    }

    public sealed record DecisionBody(string? Status);

    /// <summary><c>GET /api/quotes?status=checked[,negotiating…]|any</c>: the 4.0 offers (Oggi feed, Chiedi picker).</summary>
    internal static async Task<IResult> ListAsync(
        HttpRequest request, QuoteOfferService offers, ICallerContext callerContext, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var raw = request.Query["status"].ToString();
        var statuses = raw is "" or "any"
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (statuses.Any(s => !Raffa.Quotes.Domain.QuoteDecisionStatus.All.Contains(s)))
        {
            return Results.BadRequest("'status' must be any, or a comma list of checked, negotiating, signing, signed, declined.");
        }

        var items = await offers.ListAsync(caller.TenantId, statuses, cancellationToken).ConfigureAwait(false);
        return Results.Ok(items.Select(i => new
        {
            id = i.QuoteId.Value,
            reference = i.Reference,
            supplier = i.Supplier,
            fileName = i.FileName,
            kind = i.Kind,
            status = i.Status,
            contractId = i.ContractId?.Value,
            caseId = i.CaseId?.Value,
            validUntil = i.ValidUntil,
            quoteDate = i.QuoteDate,
            checkedBy = i.CheckedBy,
            annualTotal = i.AnnualTotal,
            annualTotalLabel = i.AnnualTotalLabel,
        }));
    }

    private static async Task<IResult> GetAsync(
        string id, HttpRequest request, QuoteOfferService offers, ICallerContext callerContext, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        if (!Guid.TryParse(id, out var guid))
        {
            return Results.BadRequest("The quote id in the route must be a GUID.");
        }

        var view = await offers.GetAsync(caller.TenantId, new EntityId(guid), Lang(request), cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(ToResponse(view));
    }

    private static async Task<IResult> ConfirmAsync(
        string id, HttpRequest request, QuoteOfferService offers, ICallerContext callerContext, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        if (!Guid.TryParse(id, out var guid))
        {
            return Results.BadRequest("The quote id in the route must be a GUID.");
        }

        var view = await offers.ConfirmExtractionAsync(caller.TenantId, new EntityId(guid), caller.Identity!, Lang(request), cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(ToResponse(view));
    }

    private static async Task<IResult> DecideAsync(
        string id, DecisionBody? body, HttpRequest request, QuoteOfferService offers, ICallerContext callerContext, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        if (!Guid.TryParse(id, out var guid))
        {
            return Results.BadRequest("The quote id in the route must be a GUID.");
        }

        var outcome = await offers
            .DecideAsync(caller.TenantId, new EntityId(guid), body?.Status ?? "", caller.Identity!, Lang(request), cancellationToken)
            .ConfigureAwait(false);
        return outcome.Result switch
        {
            QuoteDecisionResult.Ok => Results.Ok(ToResponse(outcome.View!)),
            QuoteDecisionResult.NotFound => Results.NotFound(),
            QuoteDecisionResult.InvalidStatus => Results.BadRequest("'status' must be checked, negotiating, signing, signed or declined."),
            _ => Results.Conflict("This decision cannot follow the current one."),
        };
    }

    private static async Task<IResult> CounterProposalAsync(
        string id,
        HttpRequest request,
        QuoteOfferService offers,
        ICallerContext callerContext,
        IdentityWorkspaceDbContext workspaces,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        if (!Guid.TryParse(id, out var guid))
        {
            return Results.BadRequest("The quote id in the route must be a GUID.");
        }

        var tenantId = caller.TenantId;
        var organisation = await workspaces.Workspaces.AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var by = request.HttpContext.User.FindFirst("name")?.Value
            ?? request.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value
            ?? caller.Identity!;
        var proposal = await offers
            .CounterProposalAsync(tenantId, new EntityId(guid), caller.Identity!, by, organisation, cancellationToken)
            .ConfigureAwait(false);
        return proposal is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                available = proposal.Available,
                caseId = proposal.CaseId?.Value,
                emailId = proposal.EmailId?.Value,
                subject = proposal.Subject,
                body = proposal.Body,
            });
    }

    private static string Lang(HttpRequest request) => request.Query["lang"].ToString() == "en" ? "en" : "it";

    private static object ToResponse(QuoteOfferView v) => new
    {
        id = v.QuoteId.Value,
        reference = v.Reference,
        supplier = v.Supplier,
        fileName = v.FileName,
        kind = v.Kind,
        status = v.Status,
        term = v.Term,
        valid = v.Valid,
        validUntil = v.ValidUntil,
        quoteDate = v.QuoteDate,
        checkedBy = v.CheckedBy,
        decidedBy = v.DecidedBy,
        decidedAt = v.DecidedAt,
        contractId = v.ContractId?.Value,
        caseId = v.CaseId?.Value,
        extractionConfirmedAt = v.ExtractionConfirmedAt,
        lines = v.Lines,
        stated = v.Stated,
        assessment = v.Assessment,
    };
}
