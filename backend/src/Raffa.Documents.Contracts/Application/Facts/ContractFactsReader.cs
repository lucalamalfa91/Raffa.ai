using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Facts;

/// <summary><see cref="IContractFactsReader"/> over <c>contract_facts</c> (tenant-scoped, RLS). When a
/// contract has a current <c>contract_term_version</c> (a renewal closed as renewed), its dates,
/// notice and spend replace the ones read at validation (the prototype's <c>eff(id)</c>).</summary>
public sealed class ContractFactsReader(DocumentsContractsDbContext dbContext, ITenantContext tenantContext, IClock clock)
    : IContractFactsReader
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var rows = await dbContext.ContractFacts.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.ValidatedAt != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var versions = await CurrentVersionsAsync(tenantId, rows.Select(r => r.ContractId).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var list = rows
            .Select(r => Apply(Deserialize(r.Facts, r.ContractId, r.ValidatedAt), versions.GetValueOrDefault(r.ContractId)))
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
        if (row is null)
        {
            return null;
        }

        var versions = await CurrentVersionsAsync(tenantId, [row.ContractId], cancellationToken).ConfigureAwait(false);
        var facts = Apply(Deserialize(row.Facts, row.ContractId, row.ValidatedAt), versions.GetValueOrDefault(row.ContractId));
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

    public static string Serialize(ContractFacts facts) => JsonSerializer.Serialize(facts, Json);

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
        return facts with
        {
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
            Auto = version.AutoRenew is { } auto ? (auto ? "Yes" : "No") : facts.Auto,
            Uplift = version.UpliftPct is { } uplift
                ? uplift.ToString("0.##", CultureInfo.InvariantCulture) + "%"
                : facts.Uplift,
        };
    }

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
        (JsonSerializer.Deserialize<ContractFacts>(json, Json) ?? new ContractFacts()) with
        {
            ContractId = contractId.Value,
            ValidatedAt = validatedAt,
        };
}
