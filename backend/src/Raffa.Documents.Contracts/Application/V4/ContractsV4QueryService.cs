using Raffa.Documents.Contracts.Application.Facts;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Documents.Contracts.Application.V4;

/// <summary>Contratti row status tag: <c>validated | caseOpen | renewed | terminated | autoRenewed | expired</c>.</summary>
public static class ContractRowStatus
{
    public const string Validated = "validated";
    public const string CaseOpen = "caseOpen";
    public const string Renewed = "renewed";
    public const string Terminated = "terminated";

    /// <summary>No case, and the term rolled over by itself (*Rinnovato automaticamente*).</summary>
    public const string AutoRenewed = "autoRenewed";

    /// <summary>No case, no automatic renewal, end date passed (*Scaduto*).</summary>
    public const string Expired = "expired";

    public static string Of(ContractCaseStatus status, EffectiveContractTerm? term = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.Outcome switch
        {
            ContractCaseOutcome.Renewed => Renewed,
            ContractCaseOutcome.Terminated => Terminated,
            _ when status.HasCase => CaseOpen,
            _ => term?.Status switch
            {
                ContractTermStatus.RolledOver => AutoRenewed,
                ContractTermStatus.Expired => Expired,
                _ => Validated,
            },
        };
    }
}

public sealed record ContractListRowV4(
    Guid ContractId,
    string Supplier,
    string Name,
    string NoticeDeadline,
    int NoticeDays,
    string End,
    decimal AnnualValue,
    string Currency,
    string Status,
    Guid? CaseId,
    string TermStatus = ContractTermStatus.Active,
    string? RenewedOn = null,
    bool RenewedOnPassed = false);

/// <summary><c>GET /api/contracts?view=v4</c>: validated contracts nearest notice first, and the
/// banner for a document being read or reviewed.</summary>
public sealed record ContractListV4(IReadOnlyList<ContractListRowV4> Rows, DocumentAttention? Document);

public sealed record ContractDatesV4(
    string NoticeDeadline,
    int NoticeDays,
    string End,
    bool AutoRenew,
    int? RenewalMonths,
    string Uplift,
    string Start,
    string TermStatus = ContractTermStatus.Active,
    string? RenewedOn = null,
    bool RenewedOnPassed = false,
    string? OriginalEnd = null,
    string? OriginalNoticeDeadline = null);

/// <summary>The *Prossima azione* row: the case's next action, or how to start one.</summary>
public sealed record ContractNextActionV4(bool HasCase, Guid? CaseId, string? Outcome, ContractMessage Text);

public sealed record ContractProductV4(string Name, string Unit, decimal Quantity, decimal UnitPrice, decimal Annual);

public sealed record ContractObligationV4(string Text, string When, bool High);

public sealed record ContractStdTermV4(string Type, string Normalized, string Src);

/// <summary><c>GET /api/contracts/{id}/v4</c>: Contract 360, decisions first.</summary>
public sealed record Contract360V4(
    Guid ContractId,
    string Supplier,
    string Name,
    string Type,
    int Version,
    bool Validated,
    decimal AnnualValue,
    string Currency,
    ContractDatesV4 Dates,
    IReadOnlyList<DecisionPointView> DecisionPoints,
    ContractNextActionV4 NextAction,
    IReadOnlyList<ContractTerm> Terms,
    IReadOnlyList<ContractProductV4> Products,
    IReadOnlyList<ContractObligationV4> OblYou,
    IReadOnlyList<ContractObligationV4> OblSup,
    IReadOnlyList<ContractStdTermV4> Std,
    ContractAssessmentView? Assessment = null);

/// <summary>The AskRaffa 4.0 read models of Contratti and Contratto, built only from
/// <see cref="IContractFactsReader"/> (current term version included) and the case status.</summary>
public sealed class ContractsV4QueryService(
    IContractFactsReader factsReader,
    IContractCaseStatusReader caseStatusReader,
    ContractDecisionPointService decisionPoints,
    DocumentReadingService readingService,
    IClock clock,
    IContractSituationAssessor? situationAssessor = null)
{
    /// <summary>Contracts whose notice deadline is this close have to be started now (prototype: 120 days).</summary>
    public const int StartWindowDays = 120;

    public async Task<ContractListV4> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var facts = await factsReader.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var cases = await caseStatusReader.GetAsync(tenantId, facts.Select(f => f.ContractId).ToList(), cancellationToken)
            .ConfigureAwait(false);
        var today = Today;
        var rows = facts
            .Select(f =>
            {
                var status = cases.GetValueOrDefault(f.ContractId) ?? ContractCaseStatus.None(f.ContractId);
                var term = ContractTermCalculator.Compute(f, today);
                var rolled = term.RolledOver;
                return new ContractListRowV4(
                    f.ContractId,
                    f.Supplier,
                    f.Name,
                    rolled ? ContractFactsReader.FormatDate(term.NoticeDeadline!.Value) : f.Cancel,
                    rolled ? term.NoticeDays!.Value : NoticeDays(f, today),
                    rolled ? ContractFactsReader.FormatDate(term.End!.Value) : f.End,
                    f.Spend,
                    CurrencyOf(f),
                    ContractRowStatus.Of(status, term),
                    status.CaseId,
                    term.Status,
                    term.RenewedOn is { } r ? ContractFactsReader.FormatDate(r) : null,
                    term.RenewedOn is { } rp && rp <= today);
            })
            // A contract whose notice date was not read has no deadline to sort by (its NoticeDays is 0, not
            // "due today"): it goes last, as on the Renewals board, and the screen reads "Not stated".
            .OrderBy(r => string.IsNullOrEmpty(r.NoticeDeadline))
            .ThenBy(r => r.NoticeDays)
            .ThenBy(r => r.Supplier, StringComparer.Ordinal)
            .ToList();
        var attention = await readingService.GetAttentionAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return new ContractListV4(rows, attention);
    }

    public async Task<Contract360V4?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        var facts = await factsReader.GetAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
        if (facts is null)
        {
            return null;
        }

        var cases = await caseStatusReader.GetAsync(tenantId, [facts.ContractId], cancellationToken).ConfigureAwait(false);
        var status = cases.GetValueOrDefault(facts.ContractId) ?? ContractCaseStatus.None(facts.ContractId);
        var points = await decisionPoints.ListAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false) ?? [];
        var today = Today;
        var term = ContractTermCalculator.Compute(facts, today);
        var rolled = term.RolledOver;
        var noticeDays = rolled ? term.NoticeDays!.Value : NoticeDays(facts, today);
        var renewalMonths = ContractTermCalculator.RenewalMonths(facts);
        string D(DateOnly? d, string fallback) => d is { } v ? ContractFactsReader.FormatDate(v) : fallback;
        var dates = new ContractDatesV4(
            rolled ? D(term.NoticeDeadline, facts.Cancel) : facts.Cancel,
            noticeDays,
            rolled ? D(term.End, facts.End) : facts.End,
            facts.Auto == "Yes",
            renewalMonths,
            facts.Uplift,
            rolled ? D(term.Start, facts.Start) : facts.Start,
            term.Status,
            term.RenewedOn is { } r ? ContractFactsReader.FormatDate(r) : null,
            term.RenewedOn is { } rp && rp <= today,
            rolled ? D(term.OriginalEnd, facts.End) : null,
            rolled ? D(term.OriginalNoticeDeadline, facts.Cancel) : null);
        // The judgement (model-backed, guarded) only once the notice deadline has passed: an active
        // term reads as before.
        ContractAssessmentView? assessment = null;
        if (term.Status != ContractTermStatus.Active && !status.HasCase)
        {
            var assessor = situationAssessor ?? new RulesContractSituationAssessor();
            assessment = ContractAssessmentView.Of(await assessor.AssessAsync(tenantId, facts, today, cancellationToken).ConfigureAwait(false));
        }

        var deep = facts.Deep;
        return new Contract360V4(
            facts.ContractId,
            facts.Supplier,
            facts.Name,
            facts.Type,
            1,
            facts.ValidatedAt is not null,
            facts.Spend,
            CurrencyOf(facts),
            dates,
            points,
            NextAction(facts, status, noticeDays, term),
            deep.Terms,
            deep.Products.Select(p => new ContractProductV4(p.Name, p.Unit, p.Quantity, p.UnitPrice, p.Annual)).ToList(),
            deep.OblYou.Select(Obligation).ToList(),
            deep.OblSup.Select(Obligation).ToList(),
            deep.Std.Select(s => new ContractStdTermV4(s.Topic, s.Says, s.Source)).ToList(),
            assessment);
    }

    /// <summary>The prototype's <c>c3.next</c>: the case's next action, else start it (now when
    /// the notice deadline is within <see cref="StartWindowDays"/>, else by 60 days before it).
    /// With a <paramref name="term"/> whose notice deadline passed: the next deadline of a rolled-over
    /// term, the end of an ending one, nothing to start for an expired one, the term to confirm.</summary>
    public static ContractNextActionV4 NextAction(ContractFacts facts, ContractCaseStatus status, int noticeDays, EffectiveContractTerm? term = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(status);
        if (status.HasCase)
        {
            return new ContractNextActionV4(true, status.CaseId, status.Outcome,
                status.NextAction ?? ContractMessage.Of("Open the case⟦contract⟧"));
        }

        switch (term?.Status)
        {
            case ContractTermStatus.Expired:
                return new ContractNextActionV4(false, null, null,
                    ContractMessage.Of("Expired on {date}: plan a new contract if you still use the service", ("date", ContractFactsReader.FormatDate(term.End!.Value))));
            case ContractTermStatus.Ending:
                return new ContractNextActionV4(false, null, null,
                    ContractMessage.Of("Negotiate a new contract before {date}", ("date", ContractFactsReader.FormatDate(term.End!.Value))));
            case ContractTermStatus.NeedsReview:
                return new ContractNextActionV4(false, null, null, ContractMessage.Of("Confirm the renewal term"));
        }

        var deadline = term is { RolledOver: true } ? term.NoticeDeadline : facts.NoticeDeadline;
        if (noticeDays <= StartWindowDays || deadline is null)
        {
            return new ContractNextActionV4(false, null, null, ContractMessage.Of("Start the renewal and assign an owner"));
        }

        var by = ContractFactsReader.FormatDate(deadline.Value.AddDays(-60));
        return new ContractNextActionV4(false, null, null, ContractMessage.Of("Start the renewal by {date}", ("date", by)));
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /// <summary>The contract's own currency; CHF only for facts built outside the reader (test fakes).</summary>
    public static string CurrencyOf(ContractFacts facts) =>
        string.IsNullOrWhiteSpace(facts.Currency) ? Raffa.SharedKernel.Savings.WorkspaceFinanceSettings.DefaultCurrency : facts.Currency;

    private static int NoticeDays(ContractFacts facts, DateOnly today) =>
        facts.NoticeDeadline is { } deadline ? deadline.DayNumber - today.DayNumber : facts.CancelDays;

    private static ContractObligationV4 Obligation(ContractObligation o) =>
        new(o.What, o.When, string.Equals(o.Priority, "High", StringComparison.OrdinalIgnoreCase));
}
