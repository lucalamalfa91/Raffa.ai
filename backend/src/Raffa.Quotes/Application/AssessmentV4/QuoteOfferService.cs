using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Renewals;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Quotes.Application.AssessmentV4;

/// <summary>
/// AskRaffa 4.0 Offerta (README §8; BACKEND-SPEC §2.11, §3.3): the quote as the three steps read it
/// (Verifica → Confronta → Decidi), its decision, the extraction confirmation and the
/// counter-proposal hand-off to the renewal case. The comparison is <see cref="QuoteAssessorV4"/>
/// against the customer's own contract (<see cref="IContractFactsReader"/>), the case goals
/// (<see cref="ICaseGoalsReader"/>) and the other offers received for the same contract — never the
/// market benchmark the older <c>/assessment</c> endpoint computes.
/// </summary>
public sealed class QuoteOfferService(
    QuotesDbContext dbContext,
    ITenantContext tenantContext,
    IContractFactsReader contractFacts,
    ICaseGoalsReader caseGoals,
    ICaseEmailDraftSink emailDrafts,
    IClock clock,
    IAuditWriter auditWriter)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The order the offer's terms are shown in (<c>SQ.QUOTES[].stated</c>).</summary>
    private static readonly string[] StatedOrder = ["payment", "uplift", "renewal", "liability", "sla"];

    /// <summary>The offers of the 4.0 flow (those with a decision status), newest first;
    /// <paramref name="statuses"/> empty = every status.</summary>
    public async Task<IReadOnlyList<QuoteOfferListItem>> ListAsync(
        TenantId tenantId, IReadOnlyCollection<string> statuses, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var query = dbContext.Quotes.AsNoTracking().Where(q => q.TenantId == tenantId && q.Status != null);
        if (statuses.Count > 0)
        {
            var list = statuses.ToList();
            query = query.Where(q => list.Contains(q.Status!));
        }

        var quotes = await query
            .OrderByDescending(q => q.QuoteDate)
            .ThenByDescending(q => q.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var ids = quotes.Select(q => q.Id).ToList();
        var lines = await dbContext.QuoteLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && ids.Contains(l.QuoteId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return quotes.Select(q =>
        {
            var total = lines.Where(l => l.QuoteId == q.Id).Sum(l => AnnualOf(l));
            return new QuoteOfferListItem(
                q.Id, q.Reference, q.Supplier ?? "", q.FileName, q.Kind, q.Status!, q.ContractId, q.CaseId,
                q.ValidUntil, q.QuoteDate, q.CheckedBy, total, QuoteFormat.K(total));
        }).ToList();
    }

    public async Task<QuoteOfferView?> GetAsync(
        TenantId tenantId, EntityId quoteId, string lang, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var loaded = await LoadAsync(tenantId, quoteId, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            return null;
        }

        var (quote, lines) = loaded.Value;
        var (input, assessment) = await AssessAsync(tenantId, quote, lines, lang, cancellationToken).ConfigureAwait(false);
        return ToView(quote, input, assessment);
    }

    public async Task<QuoteOfferView?> ConfirmExtractionAsync(
        TenantId tenantId, EntityId quoteId, string actor, string lang, CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginScope(tenantId))
        {
            var quote = await FindV4Async(tenantId, quoteId, cancellationToken).ConfigureAwait(false);
            if (quote is null)
            {
                return null;
            }

            if (quote.ExtractionConfirmedAt is null)
            {
                quote.ExtractionConfirmedAt = clock.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await AuditAsync(tenantId, actor, "quote.extraction.confirmed", quoteId, null, cancellationToken).ConfigureAwait(false);
            }
        }

        return await GetAsync(tenantId, quoteId, lang, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records the decision on this proposal (not on the contract, not an email).</summary>
    public async Task<QuoteDecisionOutcome> DecideAsync(
        TenantId tenantId, EntityId quoteId, string status, string actor, string lang, CancellationToken cancellationToken = default)
    {
        if (!QuoteDecisionStatus.All.Contains(status))
        {
            return new QuoteDecisionOutcome(QuoteDecisionResult.InvalidStatus, null);
        }

        using (tenantContext.BeginScope(tenantId))
        {
            var quote = await FindV4Async(tenantId, quoteId, cancellationToken).ConfigureAwait(false);
            if (quote is null)
            {
                return new QuoteDecisionOutcome(QuoteDecisionResult.NotFound, null);
            }

            if (quote.Status != status)
            {
                if (!QuoteDecisionStatus.CanMove(quote.Status!, status))
                {
                    return new QuoteDecisionOutcome(QuoteDecisionResult.InvalidTransition, null);
                }

                var from = quote.Status;
                quote.Status = status;
                var reopened = status == QuoteDecisionStatus.Checked;
                quote.DecidedBy = reopened ? null : actor;
                quote.DecidedAt = reopened ? null : clock.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await AuditAsync(tenantId, actor, "quote.decision", quoteId, $"from={from} to={status}", cancellationToken).ConfigureAwait(false);
            }
        }

        var view = await GetAsync(tenantId, quoteId, lang, cancellationToken).ConfigureAwait(false);
        return new QuoteDecisionOutcome(QuoteDecisionResult.Ok, view);
    }

    /// <summary>
    /// "Prepara la controproposta": the counter-proposal text (<c>SQ.counterEmail</c>) handed to the
    /// renewal case as a draft email — never sent from here. Not available when no case module takes it.
    /// </summary>
    public async Task<QuoteCounterProposal?> CounterProposalAsync(
        TenantId tenantId, EntityId quoteId, string actor, string actorName, string? organisation, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var loaded = await LoadAsync(tenantId, quoteId, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            return null;
        }

        var (quote, lines) = loaded.Value;
        var (input, assessment) = await AssessAsync(tenantId, quote, lines, "en", cancellationToken).ConfigureAwait(false);
        var body = QuoteAssessorV4.CounterEmail(input, assessment, actorName, organisation);
        // The supplier's commercial name (and the quote's own reference when it has one), never the
        // uploaded file name: the subject goes to the supplier.
        var who = string.IsNullOrWhiteSpace(quote.Supplier) ? "Your proposal" : quote.Supplier.Trim();
        var subject = $"Re: {who}{(string.IsNullOrWhiteSpace(quote.Reference) ? "" : " " + quote.Reference.Trim())} — counter-proposal";
        var draft = await emailDrafts.CreateDraftAsync(
                tenantId,
                new CaseEmailDraftRequest(quote.CaseId, quote.ContractId, subject, body, $"quote:{quoteId.Value}", actor),
                cancellationToken)
            .ConfigureAwait(false);
        if (draft.Available)
        {
            await AuditAsync(tenantId, actor, "quote.counter_proposal.drafted", quoteId, $"caseId={draft.CaseId?.Value}", cancellationToken).ConfigureAwait(false);
        }

        return new QuoteCounterProposal(draft.Available, draft.CaseId, draft.EmailId, subject, body);
    }

    private async Task<Quote?> FindV4Async(TenantId tenantId, EntityId quoteId, CancellationToken cancellationToken) =>
        await dbContext.Quotes
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId && q.Status != null, cancellationToken)
            .ConfigureAwait(false);

    private async Task<(Quote Quote, List<QuoteLine> Lines)?> LoadAsync(TenantId tenantId, EntityId quoteId, CancellationToken cancellationToken)
    {
        var quote = await dbContext.Quotes.AsNoTracking()
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId && q.Status != null, cancellationToken)
            .ConfigureAwait(false);
        if (quote is null)
        {
            return null;
        }

        var lines = await dbContext.QuoteLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.QuoteId == quoteId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return (quote, lines);
    }

    private async Task<(OfferInput Input, QuoteAssessment Assessment)> AssessAsync(
        TenantId tenantId, Quote quote, List<QuoteLine> lines, string lang, CancellationToken cancellationToken)
    {
        ContractFacts? contract = quote.ContractId is { } cid
            ? await contractFacts.GetAsync(tenantId, cid, cancellationToken).ConfigureAwait(false)
            : null;

        var offerLines = lines.Select(l =>
        {
            decimal? existing = l.ExistingUnitPrice;
            var label = l.ExistingPriceLabel;
            if (existing is null && contract is not null && !string.IsNullOrWhiteSpace(l.Sku))
            {
                // The customer's own price for the same SKU, from the validated contract.
                var product = contract.Deep.Products.FirstOrDefault(p => string.Equals(p.Sku, l.Sku, StringComparison.OrdinalIgnoreCase));
                if (product is not null)
                {
                    existing = product.UnitPrice;
                    label = "Your current price";
                }
            }

            return new OfferLine(l.Description, l.Sku, l.Quantity ?? 0, l.Unit ?? "", l.UnitPrice ?? 0, existing, label);
        }).ToList();

        var input = new OfferInput(
            quote.Supplier ?? "",
            quote.FileName,
            quote.TermLabel,
            quote.ValidUntil is { } v ? QuoteFormat.Display(v) : null,
            offerLines,
            ParseStated(quote.StatedTermsJson),
            ParseGaps(quote.MissingJson),
            ParseTarget(quote.TargetJson));

        var goals = quote.CaseId is not null || quote.ContractId is not null
            ? await caseGoals.GetAsync(tenantId, quote.CaseId, quote.ContractId, cancellationToken).ConfigureAwait(false)
            : null;
        OfferTarget? target = goals?.Target switch
        {
            "p5" => new OfferTarget("p5", "−5%", null),
            "p10" => new OfferTarget("p10", "−10%", null),
            "budget" => new OfferTarget("budget", "budget −10%", contract is null ? null : contract.Spend * 0.9m),
            _ => null,
        };

        var others = await OtherOffersAsync(tenantId, quote, cancellationToken).ConfigureAwait(false);
        var options = new AssessOptions(
            lang == "it" ? "it" : "en",
            target,
            goals?.PlanTermMonths?.ToString(CultureInfo.InvariantCulture),
            others,
            contract?.NoticeDeadline is { } nd ? QuoteFormat.Display(nd) : null,
            goals?.PolicyLiabilityMinMonths);
        return (input, QuoteAssessorV4.Assess(input, options));
    }

    /// <summary>Other offers actually received for the same contract (not declined).</summary>
    private async Task<IReadOnlyList<OtherOffer>> OtherOffersAsync(TenantId tenantId, Quote quote, CancellationToken cancellationToken)
    {
        if (quote.ContractId is not { } contractId)
        {
            return [];
        }

        var others = await dbContext.Quotes.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.Id != quote.Id && q.ContractId == contractId
                        && q.Status != null && q.Status != QuoteDecisionStatus.Declined)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (others.Count == 0)
        {
            return [];
        }

        var ids = others.Select(o => o.Id).ToList();
        var lines = await dbContext.QuoteLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && ids.Contains(l.QuoteId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return others.Select(o => new OtherOffer(
            (o.Supplier ?? "") + (o.Reference is null ? "" : " " + o.Reference),
            lines.Where(l => l.QuoteId == o.Id).Sum(AnnualOf),
            new string((o.TermLabel ?? "").TakeWhile(char.IsDigit).ToArray()))).ToList();
    }

    private static decimal AnnualOf(QuoteLine l) =>
        (l.UnitPrice ?? 0) * (l.Quantity ?? 0) * ((l.Unit ?? "").Contains("/ mo", StringComparison.Ordinal) ? 12m : 1m);

    private static IReadOnlyList<KeyValuePair<string, string?>> ParseStated(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        // jsonb does not keep key order: show the terms in the prototype's order, then any other.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .Select(p => new KeyValuePair<string, string?>(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null))
            .OrderBy(kv => Array.IndexOf(StatedOrder, kv.Key) is var i && i >= 0 ? i : StatedOrder.Length)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<BiText> ParseGaps(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<BiText>>(json, Json) ?? [];

    private static OfferTarget? ParseTarget(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OfferTarget>(json, Json);

    private static QuoteOfferView ToView(Quote q, OfferInput input, QuoteAssessment a) => new(
        q.Id,
        q.Reference,
        input.Supplier,
        q.FileName,
        q.Kind,
        q.Status!,
        input.Term,
        input.Valid,
        q.ValidUntil,
        q.QuoteDate,
        q.CheckedBy,
        q.DecidedBy,
        q.DecidedAt,
        q.ContractId,
        q.CaseId,
        q.ExtractionConfirmedAt,
        a.Lines.Select(l => new QuoteOfferLineView(
            l.Line.Name, l.Line.Sku, l.Line.Qty, l.Line.Unit, l.Line.Price, l.Annual, l.Line.Existing, l.Line.ExistingLabel, l.Delta,
            QuoteFormat.Num(l.Line.Qty) + " " + l.Line.Unit,
            "CHF " + QuoteFormat.Up(l.Line.Price),
            QuoteFormat.K(l.Annual))).ToList(),
        input.Stated.Select(kv => new QuoteStatedTerm(kv.Key, kv.Value)).ToList(),
        new QuoteAssessmentView(
            a.Verdict,
            a.Basis,
            a.Total,
            QuoteFormat.K(a.Total),
            a.CurTotal,
            a.CurTotal is { } ct ? QuoteFormat.K(ct) : null,
            a.Own,
            a.Own is { } own ? QuoteFormat.Pct(own) : null,
            a.Partial,
            a.Target,
            a.TargetTotal,
            a.TargetMet,
            a.Points,
            a.Missing,
            a.Gaps,
            a.Evaluable));

    private Task AuditAsync(TenantId tenantId, string actor, string action, EntityId quoteId, string? detail, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditEntry(tenantId, actor, action, "quote", quoteId.Value.ToString(), clock.UtcNow, detail), cancellationToken);
}

public sealed record QuoteOfferListItem(
    EntityId QuoteId,
    string? Reference,
    string Supplier,
    string FileName,
    string? Kind,
    string Status,
    EntityId? ContractId,
    EntityId? CaseId,
    DateOnly? ValidUntil,
    DateOnly? QuoteDate,
    string? CheckedBy,
    decimal AnnualTotal,
    string AnnualTotalLabel);

public sealed record QuoteOfferLineView(
    string Name,
    string? Sku,
    decimal Qty,
    string Unit,
    decimal Price,
    decimal Annual,
    decimal? Existing,
    string? ExistingLabel,
    double? Delta,
    string QtyLabel,
    string PriceLabel,
    string AnnualLabel);

/// <summary>What the offer states for one term; <see cref="Value"/> null = "non indicato".</summary>
public sealed record QuoteStatedTerm(string Key, string? Value);

public sealed record QuoteAssessmentView(
    string Verdict,
    string Basis,
    decimal Total,
    string TotalLabel,
    decimal? CurTotal,
    string? CurTotalLabel,
    double? Own,
    string? OwnLabel,
    bool Partial,
    OfferTarget? Target,
    decimal? TargetTotal,
    bool? TargetMet,
    IReadOnlyList<EvidencePoint> Points,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> Evaluable);

public sealed record QuoteOfferView(
    EntityId QuoteId,
    string? Reference,
    string Supplier,
    string FileName,
    string? Kind,
    string Status,
    string? Term,
    string? Valid,
    DateOnly? ValidUntil,
    DateOnly? QuoteDate,
    string? CheckedBy,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    EntityId? ContractId,
    EntityId? CaseId,
    DateTimeOffset? ExtractionConfirmedAt,
    IReadOnlyList<QuoteOfferLineView> Lines,
    IReadOnlyList<QuoteStatedTerm> Stated,
    QuoteAssessmentView Assessment);

public enum QuoteDecisionResult
{
    Ok,
    NotFound,
    InvalidStatus,
    InvalidTransition,
}

public sealed record QuoteDecisionOutcome(QuoteDecisionResult Result, QuoteOfferView? View);

public sealed record QuoteCounterProposal(bool Available, EntityId? CaseId, EntityId? EmailId, string Subject, string Body);
