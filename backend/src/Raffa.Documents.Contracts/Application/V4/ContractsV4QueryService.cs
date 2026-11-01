using Raffa.Documents.Contracts.Application.Facts;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Documents.Contracts.Application.V4;

/// <summary>Contratti row status tag: <c>validated | caseOpen | renewed | terminated</c>.</summary>
public static class ContractRowStatus
{
    public const string Validated = "validated";
    public const string CaseOpen = "caseOpen";
    public const string Renewed = "renewed";
    public const string Terminated = "terminated";

    public static string Of(ContractCaseStatus status) =>
        status.Outcome switch
        {
            ContractCaseOutcome.Renewed => Renewed,
            ContractCaseOutcome.Terminated => Terminated,
            _ => status.HasCase ? CaseOpen : Validated,
        };
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
    Guid? CaseId);

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
    string Start);

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
    IReadOnlyList<ContractStdTermV4> Std);

/// <summary>The AskRaffa 4.0 read models of Contratti and Contratto, built only from
/// <see cref="IContractFactsReader"/> (current term version included) and the case status.</summary>
public sealed class ContractsV4QueryService(
    IContractFactsReader factsReader,
    IContractCaseStatusReader caseStatusReader,
    ContractDecisionPointService decisionPoints,
    DocumentReadingService readingService,
    IClock clock)
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
                return new ContractListRowV4(
                    f.ContractId,
                    f.Supplier,
                    f.Name,
                    f.Cancel,
                    NoticeDays(f, today),
                    f.End,
                    f.Spend,
                    "CHF",
                    ContractRowStatus.Of(status),
                    status.CaseId);
            })
            .OrderBy(r => r.NoticeDays)
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
        var noticeDays = NoticeDays(facts, Today);
        var renewalTerm = facts.Deep.Terms.FirstOrDefault(t => t.Label == "Renewal term")?.Value;
        int? renewalMonths = renewalTerm is not null && int.TryParse(renewalTerm.Split(' ')[0], out var months) ? months : null;

        var deep = facts.Deep;
        return new Contract360V4(
            facts.ContractId,
            facts.Supplier,
            facts.Name,
            facts.Type,
            1,
            facts.ValidatedAt is not null,
            facts.Spend,
            "CHF",
            new ContractDatesV4(facts.Cancel, noticeDays, facts.End, facts.Auto == "Yes", renewalMonths, facts.Uplift, facts.Start),
            points,
            NextAction(facts, status, noticeDays),
            deep.Terms,
            deep.Products.Select(p => new ContractProductV4(p.Name, p.Unit, p.Quantity, p.UnitPrice, p.Annual)).ToList(),
            deep.OblYou.Select(Obligation).ToList(),
            deep.OblSup.Select(Obligation).ToList(),
            deep.Std.Select(s => new ContractStdTermV4(s.Topic, s.Says, s.Source)).ToList());
    }

    /// <summary>The prototype's <c>c3.next</c>: the case's next action, else start it (now when
    /// the notice deadline is within <see cref="StartWindowDays"/>, else by 60 days before it).</summary>
    public static ContractNextActionV4 NextAction(ContractFacts facts, ContractCaseStatus status, int noticeDays)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(status);
        if (status.HasCase)
        {
            return new ContractNextActionV4(true, status.CaseId, status.Outcome,
                status.NextAction ?? ContractMessage.Of("Open the case⟦contract⟧"));
        }

        if (noticeDays <= StartWindowDays || facts.NoticeDeadline is null)
        {
            return new ContractNextActionV4(false, null, null, ContractMessage.Of("Start the renewal and assign an owner"));
        }

        var by = ContractFactsReader.FormatDate(facts.NoticeDeadline.Value.AddDays(-60));
        return new ContractNextActionV4(false, null, null, ContractMessage.Of("Start the renewal by {date}", ("date", by)));
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private static int NoticeDays(ContractFacts facts, DateOnly today) =>
        facts.NoticeDeadline is { } deadline ? deadline.DayNumber - today.DayNumber : facts.CancelDays;

    private static ContractObligationV4 Obligation(ContractObligation o) =>
        new(o.What, o.When, string.Equals(o.Priority, "High", StringComparison.OrdinalIgnoreCase));
}
