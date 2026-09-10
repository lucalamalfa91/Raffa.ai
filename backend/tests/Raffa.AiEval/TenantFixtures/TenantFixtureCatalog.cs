using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Domain;

namespace Raffa.AiEval.TenantFixtures;

/// <summary>
/// The three tenant workspaces R-EVD-03 requires the golden set to run against ("&gt;= 40
/// questions x 3 tenants' fixtures"), named by the keys <c>golden/*.json</c> uses in its own
/// <c>tenantFixture</c> field.
/// </summary>
internal static class TenantFixtureKeys
{
    /// <summary>A brand-new workspace: no contracts, no suppliers. Exercises the empty-portfolio
    /// half of every gate label (upload invite instead of a portfolio hook — R-ASK-02), R-SYS-04's
    /// availability replacement (every greyed capability collapses to one upload action), and
    /// R-PORT-02 AC-2 ("an empty portfolio -&gt; upload invite, no ranking").</summary>
    public const string Empty = "empty";

    /// <summary>The seeded workspace, mirroring <c>inputs/design/prototypes/raffa-v2/app.jsx</c>
    /// <c>CONTRACTS</c>: validated Salesforce / Microsoft / AWS / DocuSign contracts. The oracle
    /// for every "answer" case in the set.</summary>
    public const string Seeded = "seeded";

    /// <summary>A workspace whose one contract is still <c>needs_review</c> — R-CMP-03 ("if the
    /// contract is needs_review, Ask says which weak facts block the comparison") and the
    /// <c>document_status</c> intent's "which documents are not askable yet" row of the prototype's
    /// own intent table.</summary>
    public const string NeedsReview = "needs-review";

    public static IReadOnlyList<string> All { get; } = [Empty, Seeded, NeedsReview];
}

/// <summary>
/// The rows one tenant fixture seeds into the InMemory stores (task E13/F06/US01/T02,
/// ask-golden-set). Deliberately a plain data description built by
/// <see cref="TenantFixtureCatalog"/> and applied by <c>AskEvalHost</c>: the same definition is
/// then also readable by the report writer, which prints the seeded portfolio next to the verdicts
/// so a HITL reader on `demo` can check an expected number against the data it was derived from
/// without opening any code.
/// </summary>
/// <param name="Key">One of <see cref="TenantFixtureKeys"/>.</param>
/// <param name="TenantId">A fresh tenant per fixture — never shared, so a cross-fixture leak would
/// be visible as an unexpected citation rather than silently plausible.</param>
/// <param name="Description">One line for the report header.</param>
/// <param name="Suppliers">Supplier identity rows (R-SUP-02) — what makes a named supplier
/// "known" to the domain gate instead of <c>needs_document</c> (R-ASK-03).</param>
/// <param name="Contracts">Validated (or, for <see cref="TenantFixtureKeys.NeedsReview"/>, not yet
/// validated) contract rows.</param>
internal sealed record TenantFixture(
    string Key,
    TenantId TenantId,
    string Description,
    IReadOnlyList<Supplier> Suppliers,
    IReadOnlyList<Contract> Contracts);

/// <summary>
/// Builds the three tenant fixtures. Every date, amount and supplier name is the prototype's own
/// (<c>raffa-v2/app.jsx</c> <c>CONTRACTS</c>), so a golden expectation can be traced straight
/// back to the design oracle rather than to a number invented for the test.
///
/// <para>
/// <b>Determinism</b>: ids are fixed GUID literals (never <see cref="Guid.NewGuid"/>) and
/// <c>CreatedAt</c> values are deliberately distinct, one second apart. <c>PortfolioQueryService
/// .GetPortfolioAsync</c> orders by <c>CreatedAt</c> descending then by <c>Id</c>; against the
/// InMemory provider that <c>ORDER BY</c> falls back to LINQ-to-Objects, whose secondary key
/// (<c>EntityId</c>, a record struct with no <see cref="IComparable"/>) would throw if two rows
/// ever tied on <c>CreatedAt</c>. Distinct timestamps mean the tie-break is never reached, and the
/// pack's item order — which decides which five items the fixture gateway echoes, and therefore
/// what <c>answerMarkdown</c> says — is fixed for every run.
/// </para>
/// </summary>
internal static class TenantFixtureCatalog
{
    private static readonly DateTimeOffset SeedInstant = AiEvalOptions.EvaluationInstant;

    public static IReadOnlyList<TenantFixture> All { get; } = [BuildEmpty(), BuildSeeded(), BuildNeedsReview()];

    public static TenantFixture Get(string key) =>
        All.FirstOrDefault(fixture => string.Equals(fixture.Key, key, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Unknown tenant fixture '{key}'. Known fixtures: {string.Join(", ", TenantFixtureKeys.All)}.");

    private static TenantFixture BuildEmpty() => new(
        TenantFixtureKeys.Empty,
        new TenantId(new Guid("a0000000-0000-4000-8000-000000000001")),
        "Brand-new workspace — zero contracts, zero suppliers; Ask has nothing validated to answer from.",
        [],
        []);

    private static TenantFixture BuildSeeded()
    {
        var tenantId = new TenantId(new Guid("a0000000-0000-4000-8000-000000000002"));

        var salesforce = NewSupplier(tenantId, "b0000000-0000-4000-8000-000000000001", "Salesforce");
        var microsoft = NewSupplier(tenantId, "b0000000-0000-4000-8000-000000000002", "Microsoft");
        var aws = NewSupplier(tenantId, "b0000000-0000-4000-8000-000000000003", "AWS");
        var docuSign = NewSupplier(tenantId, "b0000000-0000-4000-8000-000000000004", "DocuSign");

        // app.jsx CONTRACTS, verbatim: spend, start/end, cancellation deadline and auto-renewal for
        // each of the four validated contracts. CreatedAt descends in this same order, so the
        // portfolio — and therefore every pack built from it — is always Salesforce, Microsoft,
        // AWS, DocuSign.
        IReadOnlyList<Contract> contracts =
        [
            NewContract(
                tenantId, "c0000000-0000-4000-8000-000000000001", salesforce.Id, ContractDocumentType.Msa,
                "Completed", start: new DateOnly(2024, 3, 1), end: new DateOnly(2027, 1, 15),
                cancellationDeadline: new DateOnly(2026, 10, 18), annualSpend: 640_000m, autoRenewal: true,
                createdAt: SeedInstant),
            NewContract(
                tenantId, "c0000000-0000-4000-8000-000000000002", microsoft.Id, ContractDocumentType.Msa,
                "Completed", start: new DateOnly(2024, 7, 1), end: new DateOnly(2027, 6, 30),
                cancellationDeadline: new DateOnly(2027, 3, 31), annualSpend: 1_200_000m, autoRenewal: true,
                createdAt: SeedInstant.AddSeconds(-1)),
            NewContract(
                tenantId, "c0000000-0000-4000-8000-000000000003", aws.Id, ContractDocumentType.Amendment,
                "Completed", start: new DateOnly(2024, 9, 1), end: new DateOnly(2027, 8, 31),
                cancellationDeadline: new DateOnly(2027, 6, 1), annualSpend: 2_400_000m, autoRenewal: false,
                createdAt: SeedInstant.AddSeconds(-2)),
            NewContract(
                tenantId, "c0000000-0000-4000-8000-000000000004", docuSign.Id, ContractDocumentType.OrderForm,
                "Completed", start: new DateOnly(2023, 11, 1), end: new DateOnly(2026, 10, 31),
                cancellationDeadline: new DateOnly(2026, 10, 2), annualSpend: 58_000m, autoRenewal: true,
                createdAt: SeedInstant.AddSeconds(-3)),
        ];

        return new TenantFixture(
            TenantFixtureKeys.Seeded,
            tenantId,
            "Seeded workspace — four validated contracts mirroring the V2 prototype's own CONTRACTS " +
            "(Salesforce MSA, Microsoft EA, AWS EDP amendment, DocuSign order form), all CHF.",
            [salesforce, microsoft, aws, docuSign],
            contracts);
    }

    private static TenantFixture BuildNeedsReview()
    {
        var tenantId = new TenantId(new Guid("a0000000-0000-4000-8000-000000000003"));

        var salesforce = NewSupplier(tenantId, "b0000000-0000-4000-8000-000000000011", "Salesforce");

        // One contract, still needs_review: the end date, the cancellation deadline and the annual
        // spend are the critical facts the review has not accepted yet, so they are deliberately
        // absent — a question about "when does it expire" must not receive a confident date from a
        // contract that has none (Appendix C rule 10 / R-CMP-03).
        var contract = NewContract(
            tenantId, "c0000000-0000-4000-8000-000000000011", salesforce.Id, ContractDocumentType.Msa,
            "needs_review", start: new DateOnly(2024, 3, 1), end: null, cancellationDeadline: null,
            annualSpend: null, autoRenewal: true, createdAt: SeedInstant);

        return new TenantFixture(
            TenantFixtureKeys.NeedsReview,
            tenantId,
            "Needs-review workspace — one Salesforce MSA still in needs_review, its end date, notice " +
            "deadline and annual spend not yet accepted, so nothing about it is askable with confidence.",
            [salesforce],
            [contract]);
    }

    private static Supplier NewSupplier(TenantId tenantId, string id, string name) => new()
    {
        Id = new EntityId(new Guid(id)),
        TenantId = tenantId,
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Category = "software",
        Country = "CH",
        CreatedAt = SeedInstant,
        UpdatedAt = SeedInstant,
    };

    private static Contract NewContract(
        TenantId tenantId,
        string id,
        EntityId supplierId,
        ContractDocumentType type,
        string status,
        DateOnly? start,
        DateOnly? end,
        DateOnly? cancellationDeadline,
        decimal? annualSpend,
        bool autoRenewal,
        DateTimeOffset createdAt) => new()
        {
            Id = new EntityId(new Guid(id)),
            TenantId = tenantId,
            SupplierId = supplierId,
            Type = type,
            Status = status,
            Currency = "CHF",
            StartDate = start,
            EffectiveDate = start,
            EndDate = end,
            CancellationDeadline = cancellationDeadline,
            AnnualSpend = annualSpend,
            AutoRenewal = autoRenewal,
            RenewalTermMonths = 12,
            PaymentTerms = "Net 45, annual in advance",
            GoverningLaw = "CH",
            CreatedAt = createdAt,
        };
}
