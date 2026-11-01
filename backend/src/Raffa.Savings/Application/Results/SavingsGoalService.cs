using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Savings.Domain.Results;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Savings.Application.Results;

/// <summary><c>POST /api/savings/goals</c>: confirm a draft. When <see cref="Pct"/> and
/// <see cref="Annual"/> are both absent the server parses <see cref="Text"/> itself.</summary>
public sealed record ConfirmSavingsGoalRequest(
    string? Text,
    decimal? Pct,
    decimal? Annual,
    SavingsGoalScope? Scope,
    string? Period,
    ResultsMsg? Label);

/// <summary>Outcome of a confirm: the stored goal, or the draft that still needs an answer.</summary>
public sealed record ConfirmSavingsGoalOutcome(SavingsGoalView? Goal, SavingsGoalDraft? Draft, string? Error);

/// <summary>The user's savings goal (one active per user): parse, confirm, read, clear, progress.</summary>
public sealed class SavingsGoalService(
    SavingsDbContext dbContext,
    ITenantContext tenantContext,
    ResultsQueryService results,
    ICaseObjectiveSource caseObjectives,
    ISavingsGoalParser parser,
    IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SavingsGoalDraft?> ParseAsync(TenantId tenantId, string? text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var validated = await results.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await parser.ParseAsync(text.Trim(), validated, results.Today, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConfirmSavingsGoalOutcome> ConfirmAsync(TenantId tenantId, string userId, ConfirmSavingsGoalRequest request, CancellationToken cancellationToken = default)
    {
        var text = request.Text?.Trim() ?? "";
        if (text.Length == 0) return new ConfirmSavingsGoalOutcome(null, null, "text is required.");
        if (text.Length > 1000) return new ConfirmSavingsGoalOutcome(null, null, "text is longer than 1000 characters.");

        decimal? pct;
        decimal? annual;
        SavingsGoalScope scope;
        string period;
        ResultsMsg label;
        if (request.Pct is null && request.Annual is null)
        {
            var draft = await ParseAsync(tenantId, text, cancellationToken).ConfigureAwait(false);
            if (draft is null || !draft.Ok) return new ConfirmSavingsGoalOutcome(null, draft, null);
            (pct, annual, scope, period, label) = (draft.Pct, draft.Annual, draft.Scope, draft.Period, draft.Label);
        }
        else
        {
            if (request.Pct is < 0 or > 100) return new ConfirmSavingsGoalOutcome(null, null, "pct must be between 0 and 100.");
            if (request.Annual is < 0) return new ConfirmSavingsGoalOutcome(null, null, "annual must not be negative.");
            pct = request.Pct;
            annual = request.Annual;
            scope = request.Scope ?? new SavingsGoalScope(null, null, []);
            period = string.IsNullOrWhiteSpace(request.Period) ? "FY " + results.Today.Year : request.Period.Trim();
            if (period.Length > 40) return new ConfirmSavingsGoalOutcome(null, null, "period is too long.");
            label = request.Label ?? (scope.Supplier is { } s
                ? ResultsMsg.Of("{supplier}", ("supplier", s))
                : ResultsMsg.Of(DeterministicSavingsGoalParser.IdAllContracts));
        }

        using var _ = tenantContext.BeginScope(tenantId);
        var previous = await dbContext.SavingsGoals
            .Where(g => g.TenantId == tenantId && g.UserId == userId && g.Active)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var g in previous) g.Active = false;
        if (previous.Count > 0) await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var goal = new SavingsGoal
        {
            TenantId = tenantId,
            UserId = userId,
            SourceText = text,
            Pct = pct,
            AnnualAmount = annual,
            ScopeJson = JsonSerializer.Serialize(new StoredScope(scope.Supplier, scope.Categories, scope.ContractIds, label), Json),
            Period = period,
            Active = true,
            CreatedAt = clock.UtcNow,
        };
        dbContext.SavingsGoals.Add(goal);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ConfirmSavingsGoalOutcome(ToView(goal), null, null);
    }

    public async Task<SavingsGoalView?> GetActiveAsync(TenantId tenantId, string userId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var goal = await dbContext.SavingsGoals.AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.UserId == userId && g.Active)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return goal is null ? null : ToView(goal);
    }

    /// <returns>True when an active goal was removed.</returns>
    public async Task<bool> ClearAsync(TenantId tenantId, string userId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var active = await dbContext.SavingsGoals
            .Where(g => g.TenantId == tenantId && g.UserId == userId && g.Active)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var g in active) g.Active = false;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return active.Count > 0;
    }

    public async Task<SavingsGoalProgress?> GetProgressAsync(TenantId tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var goal = await GetActiveAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        if (goal is null) return null;
        var validated = await results.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var signed = await results.ListResultsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var objectives = await caseObjectives.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return SavingsGoalProgressBuilder.Build(goal, results.Today, validated, signed, objectives);
    }

    private static SavingsGoalView ToView(SavingsGoal goal)
    {
        var stored = JsonSerializer.Deserialize<StoredScope>(goal.ScopeJson, Json) ?? new StoredScope(null, null, [], null);
        var label = stored.Label ?? ResultsMsg.Of(DeterministicSavingsGoalParser.IdAllContracts);
        var periodLabel = DeterministicSavingsGoalParser.PeriodLabelOf(goal.Period);
        return new SavingsGoalView(
            goal.Id.Value,
            goal.SourceText,
            goal.Pct,
            goal.AnnualAmount,
            new SavingsGoalScope(stored.Supplier, stored.Categories, stored.ContractIds ?? []),
            goal.Period,
            periodLabel,
            label,
            DeterministicSavingsGoalParser.Title(goal.Pct, goal.AnnualAmount, label, periodLabel),
            goal.CreatedAt);
    }

    private sealed record StoredScope(string? Supplier, IReadOnlyList<string>? Categories, IReadOnlyList<Guid>? ContractIds, ResultsMsg? Label);
}
