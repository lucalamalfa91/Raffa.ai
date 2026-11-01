using Microsoft.EntityFrameworkCore;
using Raffa.Api.Fixtures;
using Raffa.Api.Infrastructure;
using Raffa.Documents.Contracts.Application.V4;
using Raffa.Documents.Contracts.Infrastructure.Fixtures;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary><c>POST /api/documents/{id}/reading/fields/{key}</c> body.</summary>
internal sealed record ReadingFieldDecisionRequest(string? Decision, string? Value);

/// <summary><c>PUT /api/contracts/{id}/decision-points/{pointKey}</c> body (<c>decision: null</c> clears it).</summary>
internal sealed record DecisionPointRequest(string? Decision);

/// <summary>
/// AskRaffa 4.0 slice 4 (Contratti, Contratto, Revisione): the document reading (stages → the
/// fields to verify → validated), the contract's decision points and the v4 read models. Same
/// guard ladder as every contract endpoint (<see cref="ICallerContext"/>: 401 / 400 / 404).
/// <c>POST /api/documents/sample</c> exists only in fixture mode (404 otherwise).
/// <c>GET /api/contracts?view=v4</c> is served by <see cref="ListV4Async"/> from the portfolio route.
/// </summary>
public static class ContractsV4EndpointExtensions
{
    public static IEndpointRouteBuilder MapContractsV4Endpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents/sample", UploadSampleAsync);
        endpoints.MapGet("/api/documents/{id}/reading", GetReadingAsync);
        endpoints.MapPost("/api/documents/{id}/reading/fields/{key}", DecideFieldAsync);
        endpoints.MapGet("/api/contracts/{id}/v4", GetContractV4Async);
        endpoints.MapGet("/api/contracts/{id}/decision-points", GetDecisionPointsAsync);
        endpoints.MapPut("/api/contracts/{id}/decision-points/{pointKey}", PutDecisionPointAsync);
        return endpoints;
    }

    /// <summary><c>GET /api/contracts?view=v4</c>: Contratti rows and the document banner.</summary>
    internal static async Task<IResult> ListV4Async(HttpRequest request, ICallerContext callerContext, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        var service = request.HttpContext.RequestServices.GetRequiredService<ContractsV4QueryService>();
        return Results.Ok(await service.ListAsync(caller.TenantId, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> UploadSampleAsync(
        HttpRequest request, ICallerContext callerContext, IServiceProvider services, CancellationToken cancellationToken)
    {
        var samples = services.GetService<FixtureSampleDocuments>();
        var reset = services.GetService<FixtureResetService>();
        if (samples is null || reset is null)
        {
            return Results.NotFound();
        }

        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        var context = reset.CreateContext();
        if (context.TenantId != caller.TenantId)
        {
            return Results.NotFound();
        }

        var documentId = await samples.UploadAsync(context, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { documentId });
    }

    private static async Task<IResult> GetReadingAsync(
        string id, HttpRequest request, ICallerContext callerContext, DocumentReadingService readingService,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        if (!Guid.TryParse(id, out var documentId))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        var view = await readingService.GetAsync(caller.TenantId, new EntityId(documentId), cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }

    private static async Task<IResult> DecideFieldAsync(
        string id, string key, ReadingFieldDecisionRequest? body, HttpRequest request, ICallerContext callerContext,
        DocumentReadingService readingService, IdentityWorkspaceDbContext identity, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        if (!Guid.TryParse(id, out var documentId))
        {
            return Results.BadRequest("The document id in the route must be a GUID.");
        }

        if (body?.Decision is not { } decision)
        {
            return Results.BadRequest("decision is required (accept | correct).");
        }

        var who = await DisplayNameAsync(identity, caller.TenantId, caller.Identity!, cancellationToken).ConfigureAwait(false);
        var result = await readingService.DecideAsync(
                caller.TenantId, new EntityId(documentId), key, decision, body.Value, caller.Identity!, who, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return Results.NotFound();
        }

        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetContractV4Async(
        string id, HttpRequest request, ICallerContext callerContext, ContractsV4QueryService service,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        if (!Guid.TryParse(id, out var contractId))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var view = await service.GetAsync(caller.TenantId, new EntityId(contractId), cancellationToken).ConfigureAwait(false);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }

    private static async Task<IResult> GetDecisionPointsAsync(
        string id, HttpRequest request, ICallerContext callerContext, ContractDecisionPointService service,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        if (!Guid.TryParse(id, out var contractId))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var points = await service.ListAsync(caller.TenantId, new EntityId(contractId), cancellationToken).ConfigureAwait(false);
        return points is null ? Results.NotFound() : Results.Ok(new { points });
    }

    private static async Task<IResult> PutDecisionPointAsync(
        string id, string pointKey, DecisionPointRequest? body, HttpRequest request, ICallerContext callerContext,
        ContractDecisionPointService service, IdentityWorkspaceDbContext identity, CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        if (!Guid.TryParse(id, out var contractId))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var who = await DisplayNameAsync(identity, caller.TenantId, caller.Identity!, cancellationToken).ConfigureAwait(false);
        var result = await service.SetAsync(caller.TenantId, new EntityId(contractId), pointKey, body?.Decision, who, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return Results.NotFound();
        }

        return result.IsSuccess ? Results.Ok(new { points = result.Value }) : Results.BadRequest(result.Error);
    }

    /// <summary>The caller's display name in this workspace (falls back to the identity).</summary>
    private static async Task<string> DisplayNameAsync(
        IdentityWorkspaceDbContext identity, TenantId tenantId, string subject, CancellationToken cancellationToken)
    {
        var name = await identity.WorkspaceUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.ExternalSubjectId == subject)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(name) ? subject : name;
    }
}
