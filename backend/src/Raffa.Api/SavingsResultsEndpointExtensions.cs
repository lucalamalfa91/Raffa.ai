using Raffa.Api.Infrastructure;
using Raffa.Savings.Application.Results;

namespace Raffa.Api;

/// <summary>
/// AskRaffa 4.0 Risultati (BACKEND-SPEC §4 Savings): verifiable results and the user's goal. No
/// response here carries an estimate: contractual results need a signed contract, realized ones an
/// invoice, objectives are the user's own targets.
/// </summary>
public static class SavingsResultsEndpointExtensions
{
    public static IEndpointRouteBuilder MapSavingsResultsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/savings/results", GetResultsAsync);
        endpoints.MapPost("/api/savings/goals/parse", ParseGoalAsync);
        endpoints.MapPost("/api/savings/goals", ConfirmGoalAsync);
        endpoints.MapGet("/api/savings/goals/active", GetActiveGoalAsync);
        endpoints.MapDelete("/api/savings/goals/active", ClearGoalAsync);
        endpoints.MapGet("/api/savings/goals/active/progress", GetGoalProgressAsync);
        endpoints.MapGet("/api/savings/spend", GetSpendAsync);
        return endpoints;
    }

    /// <summary>
    /// <c>GET /api/savings/spend?from=2024&amp;to=2028&amp;basis=accrual|cash</c> (Spesa e risparmi): the
    /// value of every validated contract per fiscal quarter, from its documented terms and its renewal
    /// clause; contracts that cannot be valued exactly are listed apart, never estimated. Fiscal years
    /// are named by the calendar year they start in; both bounds default around the current one.
    /// </summary>
    private static async Task<IResult> GetSpendAsync(
        HttpRequest request,
        int? from,
        int? to,
        string? basis,
        Raffa.Savings.Application.Spend.SpendReportQueryService spend,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null) return caller.Failure;
        using var scope = caller.Scope;
        var b = string.IsNullOrWhiteSpace(basis) ? Raffa.Savings.Application.Spend.SpendBasis.Accrual : basis.Trim();
        if (!Raffa.Savings.Application.Spend.SpendBasis.IsKnown(b)) return Results.BadRequest("basis must be accrual or cash.");
        if (from is < 1990 or > 2100 || to is < 1990 or > 2100) return Results.BadRequest("from and to must be fiscal years between 1990 and 2100.");
        if ((from is null) != (to is null)) return Results.BadRequest("from and to go together.");
        if (from is { } f && to is { } t && (t < f || t - f >= Raffa.Savings.Application.Spend.SpendCalculator.MaxFiscalYears))
        {
            return Results.BadRequest($"The report covers 1 to {Raffa.Savings.Application.Spend.SpendCalculator.MaxFiscalYears} fiscal years.");
        }

        return Results.Ok(await spend.GetAsync(caller.TenantId, from, to, b, cancellationToken).ConfigureAwait(false));
    }

    /// <summary><c>POST /api/savings/goals/parse</c> body.</summary>
    public sealed record ParseGoalRequest(string? Text);

    private static async Task<IResult> GetResultsAsync(
        HttpRequest request,
        string? period,
        ResultsQueryService results,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null) return caller.Failure;
        using var scope = caller.Scope;
        if (period is not null && period is not (ResultsReportBuilder.PeriodFiscalYear or ResultsReportBuilder.PeriodQuarter or ResultsReportBuilder.PeriodTwelveMonths))
        {
            return Results.BadRequest("period must be one of fy, q, 12m.");
        }

        return Results.Ok(await results.GetReportAsync(caller.TenantId, period, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> ParseGoalAsync(
        HttpRequest request,
        ParseGoalRequest body,
        SavingsGoalService goals,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null) return caller.Failure;
        using var scope = caller.Scope;
        if (string.IsNullOrWhiteSpace(body?.Text)) return Results.BadRequest("text is required.");
        if (body.Text.Length > 1000) return Results.BadRequest("text is longer than 1000 characters.");

        var draft = await goals.ParseAsync(caller.TenantId, body.Text, cancellationToken).ConfigureAwait(false);
        return draft is null ? Results.BadRequest("text is required.") : Results.Ok(draft);
    }

    private static async Task<IResult> ConfirmGoalAsync(
        HttpRequest request,
        ConfirmSavingsGoalRequest body,
        SavingsGoalService goals,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null) return caller.Failure;
        using var scope = caller.Scope;
        if (body is null) return Results.BadRequest("body is required.");

        var outcome = await goals.ConfirmAsync(caller.TenantId, caller.Identity!, body, cancellationToken).ConfigureAwait(false);
        if (outcome.Error is not null) return Results.BadRequest(outcome.Error);
        if (outcome.Goal is null) return Results.UnprocessableEntity(outcome.Draft);
        return Results.Ok(outcome.Goal);
    }

    private static async Task<IResult> GetActiveGoalAsync(
        HttpRequest request,
        SavingsGoalService goals,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null) return caller.Failure;
        using var scope = caller.Scope;
        var goal = await goals.GetActiveAsync(caller.TenantId, caller.Identity!, cancellationToken).ConfigureAwait(false);
        return goal is null ? Results.NoContent() : Results.Ok(goal);
    }

    private static async Task<IResult> ClearGoalAsync(
        HttpRequest request,
        SavingsGoalService goals,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null) return caller.Failure;
        using var scope = caller.Scope;
        await goals.ClearAsync(caller.TenantId, caller.Identity!, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> GetGoalProgressAsync(
        HttpRequest request,
        SavingsGoalService goals,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null) return caller.Failure;
        using var scope = caller.Scope;
        var progress = await goals.GetProgressAsync(caller.TenantId, caller.Identity!, cancellationToken).ConfigureAwait(false);
        return progress is null ? Results.NotFound("No active goal.") : Results.Ok(progress);
    }
}
