using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Facts;

/// <summary><see cref="IContractFactsReader"/> over <c>contract_facts</c> (tenant-scoped, RLS). When a
/// contract has a current <c>contract_term_version</c> (a renewal closed as renewed), its dates,
/// notice and spend replace the ones read at validation (the prototype's <c>eff(id)</c>).
///
/// <para><b>Which contracts are validated.</b> The one definition the workspace picker, the rail badges
/// and Ask's retrieval already use (<c>PortfolioQueryService.CountValidatedContractsAsync</c>, README):
/// a contract with a linked document in <c>Completed</c>. <c>contract_facts</c> is the projection those
/// contracts are read from, not a second definition: a contract whose document completed without the v4
/// review (a clean extraction, the nothing-to-review sweep, the legacy "Mark as validated", or before
/// <c>contract_facts</c> existed) gets its facts projected here and stored; a facts row whose contract no
/// longer has a completed document is not listed. Contracts, Results, Renewals and the picker then count
/// the same contracts.</para></summary>
public sealed class ContractFactsReader(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    ISupplierNameLookup? supplierNameLookup = null,
    ILogger<ContractFactsReader>? logger = null)
    : IContractFactsReader
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var validated = await ValidatedContractIdsAsync(tenantId, null, cancellationToken).ConfigureAwait(false);
        var rows = await EnsureFactsAsync(tenantId, validated, cancellationToken).ConfigureAwait(false);
        var ids = rows.Select(r => r.ContractId).ToList();
        var versions = await CurrentVersionsAsync(tenantId, ids, cancellationToken).ConfigureAwait(false);
        var currencies = await CurrenciesAsync(tenantId, ids, cancellationToken).ConfigureAwait(false);

        var list = rows
            .Select(r => Apply(WithCurrency(Deserialize(r.Facts, r.ContractId, r.ValidatedAt), currencies), versions.GetValueOrDefault(r.ContractId)))
            .ToList();
        list = await EnrichAsync(tenantId, list, cancellationToken).ConfigureAwait(false);
        return list
            .OrderBy(f => f.NoticeDeadline ?? DateOnly.MaxValue)
            .ThenBy(f => f.Supplier, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ContractFacts?> GetAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var row = await dbContext.ContractFacts.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.ContractId == contractId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null || row.ValidatedAt is null)
        {
            // A contract validated without the v4 review has no facts row yet: project it, so Contract 360
            // opens for every contract the picker counts.
            var validated = await ValidatedContractIdsAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
            row = validated.Count == 0
                ? row
                : (await EnsureFactsAsync(tenantId, validated, cancellationToken).ConfigureAwait(false)).SingleOrDefault() ?? row;
        }

        if (row is null)
        {
            return null;
        }

        var versions = await CurrentVersionsAsync(tenantId, [row.ContractId], cancellationToken).ConfigureAwait(false);
        var currencies = await CurrenciesAsync(tenantId, [row.ContractId], cancellationToken).ConfigureAwait(false);
        var facts = Apply(WithCurrency(Deserialize(row.Facts, row.ContractId, row.ValidatedAt), currencies), versions.GetValueOrDefault(row.ContractId));
        return (await EnrichAsync(tenantId, [facts], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Extraction evidence fields that can back the renewal/notice situation, by the
    /// evidence key the situation assessment looks for.</summary>
    private static readonly Dictionary<string, string> RenewalEvidenceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["renewalTermMonths"] = "x_renewterm",
        ["autoRenewal"] = "x_renew",
        ["noticePeriodDays"] = "x_notice",
        ["cancellationDeadline"] = "x_notice_deadline",
        ["endDate"] = "x_term",
    };

    /// <summary>Facts written before the renewal term was carried get it from the contract row, and
    /// facts with no renewal/notice evidence get the extraction's cited page for those fields (so
    /// the situation assessment can cite the clause instead of saying it cannot).</summary>
    private async Task<List<ContractFacts>> EnrichAsync(TenantId tenantId, List<ContractFacts> facts, CancellationToken cancellationToken)
    {
        var needTerm = facts.Where(f => f.RenewalTermMonths is null).Select(f => new EntityId(f.ContractId)).ToList();
        var needEvidence = facts
            .Where(f => !f.Evidence.Keys.Any(k => k.Contains("renew", StringComparison.OrdinalIgnoreCase) || k.Contains("notice", StringComparison.OrdinalIgnoreCase)))
            .Select(f => new EntityId(f.ContractId))
            .ToList();
        facts = await EnrichBillingPeriodsAsync(tenantId, facts, cancellationToken).ConfigureAwait(false);
        if (needTerm.Count == 0 && needEvidence.Count == 0)
        {
            return facts;
        }

        Dictionary<Guid, int?> months = needTerm.Count == 0
            ? []
            : await dbContext.Contracts.AsNoTracking()
                .Where(c => c.TenantId == tenantId && needTerm.Contains(c.Id) && c.RenewalTermMonths != null)
                .Select(c => new { c.Id, c.RenewalTermMonths })
                .ToDictionaryAsync(c => c.Id.Value, c => c.RenewalTermMonths, cancellationToken)
                .ConfigureAwait(false);
        var fields = RenewalEvidenceKeys.Keys.ToList();
        List<CitedField> evidence = needEvidence.Count == 0
            ? []
            : await dbContext.ExtractionEvidences.AsNoTracking()
                .Where(e => e.TenantId == tenantId && needEvidence.Contains(e.ContractId) && e.SourcePage != null && fields.Contains(e.FieldName))
                .Select(e => new CitedField(e.ContractId.Value, e.FieldName, e.SourcePage, e.SourceSpan, e.CreatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        return facts.Select(f =>
        {
            var term = f.RenewalTermMonths ?? months.GetValueOrDefault(f.ContractId);
            var cited = evidence.Where(e => e.ContractId == f.ContractId)
                .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(e => e.CreatedAt).First())
                .ToList();
            if (cited.Count == 0)
            {
                return term == f.RenewalTermMonths ? f : f with { RenewalTermMonths = term };
            }

            var merged = new Dictionary<string, ContractEvidence>(f.Evidence, StringComparer.Ordinal);
            foreach (var e in cited)
            {
                merged.TryAdd(RenewalEvidenceKeys[e.FieldName], new ContractEvidence(f.File, e.SourcePage ?? 0, "", "", e.SourceSpan ?? "", ""));
            }

            return f with { RenewalTermMonths = term, Evidence = merged };
        }).ToList();
    }

    private sealed record CitedField(Guid ContractId, string FieldName, int? SourcePage, string? SourceSpan, DateTimeOffset CreatedAt);

    /// <summary>Facts written before products carried their billing period get it from the contract's
    /// line items (same description and SKU), so a one-off amount is told apart from a recurring line on
    /// contracts validated earlier too. Fixture contracts keep their curated products.</summary>
    private async Task<List<ContractFacts>> EnrichBillingPeriodsAsync(TenantId tenantId, List<ContractFacts> facts, CancellationToken cancellationToken)
    {
        var need = facts
            .Where(f => f.FixtureId is null && f.Deep.Products.Any(p => p.BillingPeriod is null))
            .Select(f => new EntityId(f.ContractId))
            .ToList();
        if (need.Count == 0)
        {
            return facts;
        }

        var lines = await dbContext.ContractLineItems.AsNoTracking()
            .Where(l => l.TenantId == tenantId && need.Contains(l.ContractId) && l.BillingPeriod != null)
            .Select(l => new { l.ContractId, l.Description, l.Sku, l.BillingPeriod })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (lines.Count == 0)
        {
            return facts;
        }

        return facts.Select(f =>
        {
            var own = lines.Where(l => l.ContractId.Value == f.ContractId).ToList();
            if (own.Count == 0 || f.Deep.Products.All(p => p.BillingPeriod is not null))
            {
                return f;
            }

            var products = f.Deep.Products
                .Select(p => p.BillingPeriod is not null
                    ? p
                    : p with
                    {
                        BillingPeriod = own.FirstOrDefault(l =>
                            string.Equals(l.Description, p.Name, StringComparison.Ordinal)
                            && string.Equals(l.Sku ?? string.Empty, p.Sku, StringComparison.Ordinal))?.BillingPeriod,
                    })
                .ToList();
            return f with { Deep = f.Deep with { Products = products } };
        }).ToList();
    }

    /// <summary>The contracts with a linked <c>Completed</c> document — the same predicate as
    /// <c>PortfolioQueryService.CountValidatedContractsAsync</c> — optionally narrowed to one.</summary>
    private Task<List<EntityId>> ValidatedContractIdsAsync(TenantId tenantId, EntityId? only, CancellationToken cancellationToken)
    {
        var contracts = dbContext.Contracts.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (only is { } id)
        {
            contracts = contracts.Where(c => c.Id == id);
        }

        return contracts
            .Where(c => dbContext.Documents.Any(d =>
                d.TenantId == tenantId
                && d.ContractId.HasValue
                && d.ContractId.Value == c.Id
                && d.ProcessingStatus == DocumentProcessingStatus.Completed))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The validated facts rows of <paramref name="validated"/>, projecting and storing the ones that are
    /// missing (or were written before validation) from the contract's latest completed document. A row
    /// another request inserted first wins; the projection is then read back instead of failing.
    /// </summary>
    private async Task<List<ContractFactsRecord>> EnsureFactsAsync(
        TenantId tenantId, IReadOnlyCollection<EntityId> validated, CancellationToken cancellationToken)
    {
        if (validated.Count == 0)
        {
            return [];
        }

        var ids = validated.ToList();
        var rows = await dbContext.ContractFacts.AsNoTracking()
            .Where(f => f.TenantId == tenantId && ids.Contains(f.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var missing = ids.Where(id => !rows.Any(r => r.ContractId == id && r.ValidatedAt != null)).ToList();
        if (missing.Count == 0)
        {
            return rows;
        }

        // Document.ContractId is nullable: the list is too, so both sides use the same converter (the
        // pattern EmbeddingRetrievalService documents for the identical Npgsql array-converter limit).
        var missingNullable = missing.ConvertAll(id => (EntityId?)id);
        var documents = await dbContext.Documents.AsNoTracking()
            .Where(d => d.TenantId == tenantId && missingNullable.Contains(d.ContractId)
                && d.ProcessingStatus == DocumentProcessingStatus.Completed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var projector = new ContractFactsProjector(dbContext, clock, supplierNameLookup);
        var now = clock.UtcNow;
        foreach (var contractId in missing)
        {
            var document = documents.Where(d => d.ContractId == contractId).OrderByDescending(d => d.CreatedAt).FirstOrDefault();
            if (document is null)
            {
                continue;
            }

            var existing = await dbContext.ContractFacts
                .SingleOrDefaultAsync(f => f.TenantId == tenantId && f.ContractId == contractId, cancellationToken)
                .ConfigureAwait(false);
            // A fixture contract's curated facts are kept as they are; only their validation is recorded.
            var stored = existing is null ? null : JsonSerializer.Deserialize<ContractFacts>(existing.Facts, Json);
            var facts = stored?.FixtureId is not null
                ? stored
                : await projector.BuildAsync(tenantId, contractId, document, cancellationToken).ConfigureAwait(false);
            var json = Serialize(facts with { ValidatedAt = now });
            if (existing is null)
            {
                existing = new ContractFactsRecord { TenantId = tenantId, ContractId = contractId, Facts = json, ValidatedAt = now, UpdatedAt = now };
                dbContext.ContractFacts.Add(existing);
            }
            else
            {
                existing.Facts = json;
                existing.ValidatedAt = now;
                existing.UpdatedAt = now;
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // Another request projected the same contract first (unique tenant + contract): keep theirs.
                dbContext.Entry(existing).State = EntityState.Detached;
                existing = await dbContext.ContractFacts.AsNoTracking()
                    .SingleOrDefaultAsync(f => f.TenantId == tenantId && f.ContractId == contractId, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is null)
                {
                    continue;
                }
            }
            catch (DbUpdateException exception)
            {
                // The projection could not be stored. The contract is validated all the same (a completed
                // document): it is served from this projection, never dropped from Contracts, Renewals and
                // Results, and the failure is logged with the contract so it can be repaired.
                dbContext.Entry(existing).State = EntityState.Detached;
                logger?.LogError(exception,
                    "Contract facts of validated contract {ContractId} could not be stored; serving the projection unsaved",
                    contractId.Value);
                existing = new ContractFactsRecord { TenantId = tenantId, ContractId = contractId, Facts = json, ValidatedAt = now, UpdatedAt = now };
            }

            rows.RemoveAll(r => r.ContractId == contractId);
            rows.Add(existing);
        }

        return rows.Where(r => r.ValidatedAt != null).ToList();
    }

    public static string Serialize(ContractFacts facts) => JsonSerializer.Serialize(facts, Json);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>"18 Oct 2026" — the display form the facts carry.</summary>
    public static string FormatDate(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The facts as they run under <paramref name="version"/> (null: unchanged).</summary>
    public ContractFacts Apply(ContractFacts facts, ContractTermVersion? version)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (version is null)
        {
            return facts;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var notice = version.NoticeDays ?? facts.Notice;
        var deadline = version.ValidTo.AddDays(-notice);
        var spend = version.AnnualValue ?? facts.Spend;
        var currency = string.IsNullOrWhiteSpace(version.Currency) ? facts.Currency : version.Currency.Trim().ToUpperInvariant();
        return facts with
        {
            Deep = facts.Deep with
            {
                Terms = AlignTerms(facts.Deep.Terms, version, spend, currency, notice),
                Products = AlignProducts(facts.Deep.Products, facts.Spend, spend),
            },
            StartDate = version.ValidFrom,
            EndDate = version.ValidTo,
            NoticeDeadline = deadline,
            Start = FormatDate(version.ValidFrom),
            End = FormatDate(version.ValidTo),
            Cancel = FormatDate(deadline),
            Days = version.ValidTo.DayNumber - today.DayNumber,
            CancelDays = deadline.DayNumber - today.DayNumber,
            Notice = notice,
            Spend = version.AnnualValue ?? facts.Spend,
            Currency = string.IsNullOrWhiteSpace(version.Currency) ? facts.Currency : version.Currency.Trim().ToUpperInvariant(),
            Auto = version.AutoRenew is { } auto ? (auto ? "Yes" : "No") : facts.Auto,
            Uplift = version.UpliftPct is { } uplift
                ? uplift.ToString("0.##", CultureInfo.InvariantCulture) + "%"
                : facts.Uplift,
        };
    }

    /// <summary>The key terms that the running version states (spend, period, notice) take its values, so
    /// the header and "Full contract" describe the same version. Terms the version does not carry stay.</summary>
    private static IReadOnlyList<ContractTerm> AlignTerms(
        IReadOnlyList<ContractTerm> terms, ContractTermVersion version, decimal spend, string currency, int notice) =>
        terms.Select(t => t.Label switch
        {
            "Annual spend" when version.AnnualValue is not null =>
                t with { Value = currency + " " + spend.ToString("#,0", CultureInfo.InvariantCulture) },
            "Term" => t with { Value = $"{FormatDate(version.ValidFrom)} → {FormatDate(version.ValidTo)}" },
            "Notice period" => t with { Value = $"{notice} days" },
            _ => t,
        }).ToList();

    /// <summary>Recurring product lines follow the version's annual value: the version stores the total, not
    /// the lines, so each recurring line is scaled by the same ratio (quantity kept, unit price and annual
    /// cost follow). One-off lines are left as written.</summary>
    private static IReadOnlyList<ContractProduct> AlignProducts(IReadOnlyList<ContractProduct> products, decimal oldSpend, decimal newSpend)
    {
        if (oldSpend <= 0 || newSpend <= 0 || oldSpend == newSpend)
        {
            return products;
        }

        var ratio = newSpend / oldSpend;
        return products
            .Select(p => p.BillingPeriod is { } period && period.Contains("one", StringComparison.OrdinalIgnoreCase)
                ? p
                : p with { UnitPrice = Math.Round(p.UnitPrice * ratio, 2), Annual = Math.Round(p.Annual * ratio, 2) })
            .ToList();
    }

    /// <summary>Each contract's currency, from its own row: the one source of truth (the facts JSON
    /// may predate the field, and a fixture's curated JSON never carried it).</summary>
    private async Task<Dictionary<Guid, string>> CurrenciesAsync(
        TenantId tenantId, IReadOnlyCollection<EntityId> contractIds, CancellationToken cancellationToken)
    {
        if (contractIds.Count == 0)
        {
            return [];
        }

        var ids = contractIds.ToList();
        return await dbContext.Contracts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Currency })
            .ToDictionaryAsync(c => c.Id.Value, c => c.Currency, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ContractFacts WithCurrency(ContractFacts facts, IReadOnlyDictionary<Guid, string> currencies) =>
        currencies.TryGetValue(facts.ContractId, out var code) && !string.IsNullOrWhiteSpace(code)
            ? facts with { Currency = code.Trim().ToUpperInvariant() }
            : facts;

    private async Task<Dictionary<EntityId, ContractTermVersion>> CurrentVersionsAsync(
        TenantId tenantId, IReadOnlyCollection<EntityId> contractIds, CancellationToken cancellationToken)
    {
        if (contractIds.Count == 0)
        {
            return [];
        }

        var versions = await dbContext.ContractTermVersions.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.IsCurrent && contractIds.Contains(v.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return versions
            .GroupBy(v => v.ContractId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.CreatedAt).First());
    }

    /// <summary>The row's own columns win over what the JSON says (one source of truth for the
    /// id and the validation state).</summary>
    private static ContractFacts Deserialize(string json, EntityId contractId, DateTimeOffset? validatedAt) =>
        ContractDisplayName.Normalize((JsonSerializer.Deserialize<ContractFacts>(json, Json) ?? new ContractFacts()) with
        {
            ContractId = contractId.Value,
            ValidatedAt = validatedAt,
        });
}
