namespace Raffa.Savings.Application.Results;

/// <summary>A message not yet in any language: <c>id</c> is the English text (the web catalogue
/// translates it), <c>vars</c> fill its <c>{placeholders}</c> and may themselves be messages.</summary>
public sealed record ResultsMsg(string Id, IReadOnlyDictionary<string, object?>? Vars = null)
{
    public static ResultsMsg Of(string id) => new(id);

    public static ResultsMsg Of(string id, params (string Name, object? Value)[] vars) =>
        new(id, vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));
}

/// <summary>A signed result as the report reads it (from <c>saving_result</c> + its realizations).</summary>
public sealed record ResultsReportItem(
    Guid Id,
    string Supplier,
    string Contract,
    string? Category,
    string Kind,
    decimal Amount,
    DateOnly SignedOn,
    string Note,
    string Baseline,
    string Period,
    string Method,
    string Evidence,
    string? Owner,
    decimal RealizedAmount,
    ResultsRealizationProof? Proof);

/// <summary>What the invoices prove for one result: the invoiced months and how many of the result's
/// period they cover.</summary>
public sealed record ResultsRealizationProof(DateOnly FirstInvoice, DateOnly LastInvoice, int InvoiceCount, int? PeriodMonths);

/// <summary>One validated contract without a closed case (an open objective, possibly not set).</summary>
public sealed record ResultsOpenObjective(
    Guid ContractId,
    Guid? CaseId,
    string Supplier,
    string? Category,
    string? Owner,
    int Days,
    DateOnly? NoticeDeadline,
    decimal Spend,
    string? ObjectiveKind,
    int ObjectivePercent,
    decimal? TargetAmount,
    bool HasCase);

public sealed record ResultsNextNotice(Guid ContractId, string Supplier, DateOnly NoticeDeadline, int Days);

public sealed record ResultsHeadline(decimal Contractual, decimal Realized, decimal Volume, int Count, int VolumeCount, string Currency);

/// <summary>The KPI strip's numbers (no estimate among them).</summary>
public sealed record ResultsKpis(
    int SignedCount,
    decimal? AveragePerContract,
    int? ProvenPercent,
    decimal Realized,
    decimal Contractual,
    decimal AnalysedSpend,
    int ValidatedCount,
    decimal OpenObjectives,
    int OpenCount,
    int WithoutObjectiveCount,
    ResultsNextNotice? NextNotice);

public sealed record ResultsMonthSupplier(string Supplier, decimal Amount);

public sealed record ResultsMonth(int Year, int Month, string Label, bool Future, decimal Amount, int Count, IReadOnlyList<ResultsMonthSupplier> Items);

public sealed record ResultsBySupplier(string Label, decimal Contractual, decimal Realized, decimal Objectives, int N);

public sealed record ResultsLever(string Kind, int Count, decimal Amount);

public sealed record ResultsProof(Guid ResultId, string Supplier, decimal Amount, decimal Realized);

/// <summary>A contractual result row: baseline · period · method · evidence · realized status.</summary>
public sealed record ResultsContractualRow(
    Guid Id,
    DateOnly SignedOn,
    string Supplier,
    string Kind,
    string Note,
    string Baseline,
    string Period,
    string Method,
    string Evidence,
    decimal Amount,
    bool Realized,
    decimal RealizedAmount,
    ResultsRealizationProof? Proof);

public sealed record ResultsVolumeRow(Guid Id, DateOnly SignedOn, string Supplier, string Note, decimal Amount);

/// <summary><c>GET /api/savings/results</c>: contractual / realized / quantity reductions apart /
/// open objectives (user targets). Deliberately has no estimate field.</summary>
public sealed record ResultsReport(
    string Period,
    DateOnly Today,
    int FiscalYear,
    ResultsHeadline Headline,
    ResultsKpis Kpis,
    IReadOnlyList<ResultsMonth> Months,
    decimal Peak,
    IReadOnlyList<ResultsBySupplier> BySupplier,
    decimal MaxBySupplier,
    IReadOnlyList<ResultsLever> Levers,
    IReadOnlyList<ResultsProof> Proof,
    IReadOnlyList<ResultsOpenObjective> OpenObjectives,
    IReadOnlyList<ResultsContractualRow> Contractual,
    IReadOnlyList<ResultsVolumeRow> Volume);

/// <summary>A validated contract as Risultati needs it (from <c>IContractFactsReader</c>).</summary>
public sealed record ResultsContract(
    Guid ContractId,
    string Supplier,
    string? Category,
    decimal Spend,
    DateOnly? NoticeDeadline,
    int Days,
    string Uplift,
    bool AutoRenewal);
