namespace Raffa.SharedKernel.Contracts;

/// <summary>
/// AskRaffa 4.0 contract facts (IMPLEMENTATION-PLAN.md §2 "Contract facts"): one validated contract
/// as the guided flow reads it — the prototype's <c>RD.CONTRACTS[id]</c> fields, its
/// <c>RD.DEEP[id]</c> deep dive, the <c>RD.EVIDENCE</c> quotes it cites and the fields flagged for
/// review (<c>RD.weakFields()</c>). Serialised camelCase (<c>JsonSerializerDefaults.Web</c>) into
/// <c>contract_facts.facts</c>; Renewals, Savings, Quotes and Chat read contracts only through
/// <see cref="IContractFactsReader"/>.
/// <para>Date strings (<see cref="Start"/>, <see cref="End"/>, <see cref="Cancel"/>) keep the
/// prototype's display form ("18 Oct 2026"); the ISO forms are <see cref="StartDate"/>,
/// <see cref="EndDate"/> and <see cref="NoticeDeadline"/>. <see cref="Days"/>/<see cref="CancelDays"/>
/// are relative to the scenario clock the facts were written with — compute live values from the
/// ISO dates. <see cref="Saving"/> is carried for fidelity with the source data only: no estimated
/// saving may be shown (README "Hard product rules").</para>
/// </summary>
public sealed record ContractFacts
{
    /// <summary>The real <c>contract.id</c>.</summary>
    public Guid ContractId { get; init; }

    /// <summary>Prototype id 1..4 (Salesforce, Microsoft, AWS, DocuSign) for fixture data; null otherwise.</summary>
    public int? FixtureId { get; init; }

    public string Supplier { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string File { get; init; } = "";
    public decimal Spend { get; init; }
    public string Start { get; init; } = "";
    public string End { get; init; } = "";
    public int Days { get; init; }
    public string Cancel { get; init; } = "";
    public int CancelDays { get; init; }

    /// <summary>Notice period in days.</summary>
    public int Notice { get; init; }

    /// <summary>"Yes" | "No" (auto-renewal).</summary>
    public string Auto { get; init; } = "";

    /// <summary>Months each automatic renewal adds (null when the contract does not say, or for
    /// facts written before it was carried; <see cref="ContractTermCalculator.RenewalMonths"/> then
    /// reads the "Renewal term" key term).</summary>
    public int? RenewalTermMonths { get; init; }

    public string Risk { get; init; } = "";
    public int Score { get; init; }
    public string Uplift { get; init; } = "";
    public string Saving { get; init; } = "";
    public string Lever { get; init; } = "";
    public string Action { get; init; } = "";
    public string Rationale { get; init; } = "";
    public int DocCount { get; init; }

    /// <summary><c>SQ.CATS[id]</c>: CRM | Productivity | Cloud infrastructure | ...</summary>
    public string? Category { get; init; }

    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    /// <summary>Last day to give notice (the prototype's <c>cancel</c>).</summary>
    public DateOnly? NoticeDeadline { get; init; }

    public ContractDeepFacts Deep { get; init; } = new();

    /// <summary>Evidence quotes by key (<c>sf_renew</c>, <c>ms_liab</c>...), those this contract cites.</summary>
    public IReadOnlyDictionary<string, ContractEvidence> Evidence { get; init; } = new Dictionary<string, ContractEvidence>();

    /// <summary>The fields the reading flagged for the user's check (<c>RD.weakFields()</c>).</summary>
    public IReadOnlyList<ContractReviewField> ReviewFields { get; init; } = [];

    /// <summary>When the user validated the contract; null while it is not validated.</summary>
    public DateTimeOffset? ValidatedAt { get; init; }
}

/// <summary><c>RD.DEEP[id]</c> (after the prototype's <c>mkDeep</c>: no market column, no benchmark lever).</summary>
public sealed record ContractDeepFacts
{
    public IReadOnlyList<ContractProduct> Products { get; init; } = [];
    public string ProductNote { get; init; } = "";
    public IReadOnlyList<ContractLever> Levers { get; init; } = [];

    /// <summary>"Da discutere": clauses to negotiate.</summary>
    public IReadOnlyList<ContractClausePoint> Push { get; init; } = [];

    /// <summary>"Da sollevare": clauses to raise.</summary>
    public IReadOnlyList<ContractClausePoint> Raise { get; init; } = [];

    /// <summary>Standard terms already accepted.</summary>
    public IReadOnlyList<ContractStandardTerm> Std { get; init; } = [];
    public IReadOnlyList<ContractObligation> OblYou { get; init; } = [];
    public IReadOnlyList<ContractObligation> OblSup { get; init; } = [];
    public IReadOnlyList<ContractScorePart> ScoreParts { get; init; } = [];
    public IReadOnlyList<ContractRisk> Risks { get; init; } = [];
    public IReadOnlyList<ContractTerm> Terms { get; init; } = [];
}

/// <param name="BillingPeriod">How the line is billed as the extraction read it ("monthly", "annual",
/// "one-time", …); null when not read. See <see cref="ContractBillingPeriod"/>.</param>
public sealed record ContractProduct(string Name, string Sku, string Unit, decimal Quantity, decimal UnitPrice, decimal Annual, string? BillingPeriod = null);

public sealed record ContractLever(string Strength, string Value, string Title, string Ask, string Source);

public sealed record ContractClausePoint(string Topic, string Says, string Proposal, string Source, string? EvidenceKey);

public sealed record ContractStandardTerm(string Topic, string Says, string Source);

public sealed record ContractObligation(string What, string When, string Priority, string Recurrence);

public sealed record ContractScorePart(string Label, int Points, int Max);

public sealed record ContractRisk(string Severity, string Title, string Detail);

public sealed record ContractTerm(string Label, string Value);

/// <summary><c>RD.EVIDENCE[key]</c>: a quote with its surrounding text.</summary>
public sealed record ContractEvidence(string Doc, int Page, string Sec, string Before, string Quote, string After);

/// <summary>A field the reading is unsure of (<c>RD.weakFields()</c>); <see cref="Ev"/> is an evidence key.</summary>
public sealed record ContractReviewField(string Label, string Value, string Src, string Ev, double Conf, string Critical, string? Why);
