namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// Everything the case engine needs from outside the case: the contract's validated facts, who is
/// acting/looking, today's date (never read from a clock inside the engine) and the scripted services.
/// Language is not part of the context: every text the engine produces is a <c>CaseText</c> message.
/// </summary>
public sealed record CaseContext(CaseContractFacts Contract, string Viewer, DateOnly Today, ICaseScriptServices Services)
{
    /// <summary>The same context seen by somebody else.</summary>
    public CaseContext As(string viewer) => this with { Viewer = viewer };
}

/// <summary>
/// The contract facts the prototype reads from <c>RD.CONTRACTS[id]</c> (header facts) and
/// <c>RD.DEEP[id]</c> (products, clauses, key terms, obligations).
/// </summary>
public sealed record CaseContractFacts
{
    public int Id { get; init; }

    public string Supplier { get; init; } = string.Empty;

    /// <summary>The buyer: the workspace's company name, which signs the emails ("" = unknown).</summary>
    public string Company { get; init; } = string.Empty;

    /// <summary>The contract's category, when read ("electronic signature"); empty = unknown.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>"Sales Cloud Enterprise — MSA + Order Form".</summary>
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    /// <summary>Contract file name, attached to outgoing emails.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Annual spend, in <see cref="Currency"/>.</summary>
    public double Spend { get; init; }

    /// <summary>ISO 4217 code of the contract's money (the renewed terms and the signed result are
    /// recorded in it); "" when the facts did not carry one.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>The contract's currency, CHF when the facts carried none (the prototype's single currency).</summary>
    public string CurrencyOrDefault => string.IsNullOrWhiteSpace(Currency) ? "CHF" : Currency.Trim().ToUpperInvariant();

    public DateOnly Start { get; init; }

    public DateOnly End { get; init; }

    /// <summary>Last day to give notice.</summary>
    public DateOnly Cancel { get; init; }

    /// <summary>Notice period in days.</summary>
    public int Notice { get; init; }

    /// <summary>Renews automatically unless notice is given.</summary>
    public bool Auto { get; init; }

    /// <summary>Uplift at renewal as written in the contract ("7%", "0%").</summary>
    public string Uplift { get; init; } = "0%";

    /// <summary>Price table; the first product drives the price arithmetic.</summary>
    public IReadOnlyList<CaseProduct> Products { get; init; } = [];

    /// <summary>Clauses worth pushing on (pre-selected in the plan).</summary>
    public IReadOnlyList<CaseClause> Push { get; init; } = [];

    /// <summary>Clauses worth raising.</summary>
    public IReadOnlyList<CaseClause> Raise { get; init; } = [];

    public IReadOnlyList<CaseKeyTerm> Terms { get; init; } = [];

    public IReadOnlyList<CaseObligation> OblYou { get; init; } = [];

    public IReadOnlyList<CaseObligation> OblSup { get; init; } = [];
}

/// <summary>A price-table line: <c>[name, sku, unit, qty, price]</c>.</summary>
/// <param name="OneOff">A one-off amount (onboarding, setup): paid once, not part of what a renewal renews.</param>
public sealed record CaseProduct(string Name, string Sku, string Unit, double Qty, double Price, bool OneOff = false);

/// <summary>A clause: <c>[type, what the contract says, what to ask, source]</c>.</summary>
public sealed record CaseClause(string Type, string Norm, string Ask, string Src);

public sealed record CaseKeyTerm(string Label, string Value);

public sealed record CaseObligation(string Text, string When, string Priority = "", string Cadence = "");
