using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Documents.Contracts.Application;

/// <summary>
/// Implements task E02/F03/US01/T01 (us-01-portfolio-list-filters, AC-1/AC-2/AC-3):
/// <c>GET /api/contracts</c> — the spec §8.1 portfolio columns (see <see cref="PortfolioListItem"/>),
/// with the server-side filters spec §8.1 lists (see <see cref="PortfolioFilter"/> for the one
/// filter deliberately not supported yet, and why), scoped to the caller's tenant (ADR-009). Task
/// E02/F03/US01/T02 adds the <see cref="PortfolioPageRequest"/>/<see cref="PortfolioPage"/>
/// pagination below.
///
/// Same shape as <see cref="DocumentQueryService"/>: nothing upstream (no middleware; see
/// <c>Contigo.Api.Program</c>) opens a tenant scope before a read runs, so this opens its own
/// <see cref="ITenantContext.BeginScope"/> rather than trusting one is already active. The
/// explicit <c>Where(TenantId == tenantId)</c> below is the application-level filter ADR-009
/// additionally asks for on top of the Postgres RLS backstop the `contract`/`risk` tables already
/// carry (added by the migration that produced the `contract-schema` artifact this task depends
/// on) — belt-and-suspenders, never removed even though RLS alone would already narrow the
/// result set.
///
/// Task E04/F03/US01/T01 (savings-kpis) adds <see cref="GetAnalysisSummaryAsync"/> — the
/// "Contracts Analyzed"/"Annual Spend Analyzed" fetch half of the procurement-homepage KPIs (see
/// that method's own doc comment and <see cref="PortfolioAnalysisCalculator"/>, the pure half).
/// </summary>
public sealed class PortfolioQueryService(
    DocumentsContractsDbContext dbContext, ITenantContext tenantContext, PortfolioAnalysisCalculator analysisCalculator)
{
    public async Task<PortfolioPage> GetPortfolioAsync(
        TenantId tenantId,
        PortfolioFilter filter,
        PortfolioPageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        var pageRequest = page ?? PortfolioPageRequest.Default;

        // Entry point: open this call's own tenant scope (see the type doc comment). Must happen
        // before the query below, since the RLS connection interceptor reads
        // ITenantContext.Current only when the connection opens, which EF Core does lazily on
        // first use — i.e. inside this awaited call.
        using var _ = tenantContext.BeginScope(tenantId);

        var query = dbContext.Contracts.AsNoTracking().Where(c => c.TenantId == tenantId);

        // Nullable-to-nullable comparison (both sides EntityId?) so EF Core translates this
        // through the same NullableEntityIdConverter the column itself uses.
        if (filter.SupplierId is not null)
        {
            var supplierId = filter.SupplierId;
            query = query.Where(c => c.SupplierId == supplierId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (filter.AutoRenewal is { } autoRenewal)
        {
            query = query.Where(c => c.AutoRenewal == autoRenewal);
        }

        if (filter.MinAnnualSpend is { } minAnnualSpend)
        {
            query = query.Where(c => c.AnnualSpend != null && c.AnnualSpend >= minAnnualSpend);
        }

        if (filter.MaxAnnualSpend is { } maxAnnualSpend)
        {
            query = query.Where(c => c.AnnualSpend != null && c.AnnualSpend <= maxAnnualSpend);
        }

        // "Renewal period" only ever matches auto-renewing contracts — see PortfolioListItem's
        // own doc comment on why RenewalDate (the field this filters) is null otherwise.
        if (filter.RenewalFrom is { } renewalFrom)
        {
            query = query.Where(c => c.AutoRenewal && c.EndDate != null && c.EndDate >= renewalFrom);
        }

        if (filter.RenewalTo is { } renewalTo)
        {
            query = query.Where(c => c.AutoRenewal && c.EndDate != null && c.EndDate <= renewalTo);
        }

        // Deterministic order (task E02/F03/US01/T02): Skip/Take further down is only
        // well-defined over an ordered sequence — an unordered one lets Postgres (or the
        // in-memory List below) return a different arrangement per call, which would make two
        // consecutive page reads skip or repeat rows. Newest-first, same convention as
        // Contigo.Audit.Infrastructure.AuditQueryService; Id is a tiebreaker only (for CreatedAt
        // collisions), not itself a meaningful sort key.
        var contracts = await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Bulk-load risks for exactly the matched contracts (one extra query, not N+1) to compute
        // each row's "Risk" column as its highest-severity recorded risk. Grouping/Max happen
        // in-memory below (this list is already materialized), not translated to SQL, so
        // RiskSeverity's declared enum order (Low < Medium < High < Critical) — not its
        // `HasConversion<string>()` column representation — is what Max compares.
        var contractIds = contracts.Select(c => c.Id).ToList();
        var risksByContract = await dbContext.Risks
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && contractIds.Contains(r.ContractId))
            .Select(r => new { r.ContractId, r.Severity })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var maxSeverityByContract = risksByContract
            .GroupBy(r => r.ContractId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Severity));

        // Preserves `contracts`' own order (LINQ-to-Objects Select is order-preserving), so the
        // deterministic ORDER BY above still holds after this projection.
        IEnumerable<PortfolioListItem> items = contracts.Select(c => new PortfolioListItem(
            c.Id.Value,
            c.SupplierId?.Value,
            c.Type,
            c.AnnualSpend,
            c.Currency,
            c.StartDate,
            c.EndDate,
            c.AutoRenewal ? c.EndDate : null,
            c.CancellationDeadline,
            c.AutoRenewal,
            c.Status,
            maxSeverityByContract.TryGetValue(c.Id, out var severity) ? severity : null));

        // Risk severity filters the *computed* column above, so — unlike every other filter —
        // it is applied in-memory after the join rather than translated to SQL against the
        // `risk` table directly. It must run — and TotalCount below must be taken — *after* this
        // filter: PortfolioQueryServiceTests.Pagination_total_count_reflects_risk_filter_not_unfiltered_set
        // pins that TotalCount reflects the risk-filtered set, not every row this tenant/filter
        // combination matched before Risk was considered.
        if (filter.Risk is { } risk)
        {
            items = items.Where(i => i.Risk == risk);
        }

        // Paging (task E02/F03/US01/T02) slices the already-materialized, already-ordered list
        // rather than pushing Skip/Take to SQL: the Risk filter above has no SQL-translatable
        // representation to filter *or* count by ahead of time (see the comment above), so full
        // materialization already had to happen before this point regardless of Risk being set —
        // same as this method already did before T02. A future task can split this into a
        // SQL-paged fast path for the filter.Risk is null case once portfolio sizes make that
        // extra branch worth it.
        var matched = items.ToList();
        var totalCount = matched.Count;
        var skip = (int)Math.Min((long)(pageRequest.Page - 1) * pageRequest.PageSize, (long)totalCount);
        var pageItems = matched.Skip(skip).Take(pageRequest.PageSize).ToList();

        return new PortfolioPage(pageItems, pageRequest.Page, pageRequest.PageSize, totalCount);
    }

    /// <summary>
    /// Backs the "Contracts Analyzed"/"Annual Spend Analyzed" pair of `GET /api/savings/kpis`
    /// (task E04/F03/US01/T01, savings-kpis; product spec §10.1; parent story us-01-savings-kpis
    /// AC-1). A <see cref="Domain.Contract"/> row exists as soon as extraction *starts*, not once
    /// it finishes (<see cref="Extraction.StagedExtractionService.EnsureContractAsync"/> creates a
    /// bootstrap shell before any pipeline stage has run — see that method's own doc comment), so
    /// "analyzed" cannot mean "a Contract row exists for this tenant"; it means at least one linked
    /// <see cref="Domain.Document"/> reached <see cref="Domain.DocumentProcessingStatus.Completed"/>
    /// — spec §10.1's own KPI meaning, verbatim: "Contracts with completed processing". Two queries
    /// (documents-with-a-completed-status, then every contract), not one join, mirroring
    /// <see cref="GetPortfolioAsync"/>'s own "bulk-load the join set, then shape in memory" style
    /// above; tenant data volumes here are the same order of magnitude that method already accepts
    /// materializing in full.
    /// </summary>
    public async Task<PortfolioAnalysisSummary> GetAnalysisSummaryAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        // Plain-column projection only (no nullable-EntityId unwrap inside the translated query) —
        // the "materialize the join set first, then shape it in memory" convention GetPortfolioAsync
        // already uses above for its own Risk-severity join, extended here to the same
        // ProcessingStatus/ContractId narrowing.
        var documents = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .Select(d => new { d.ProcessingStatus, d.ContractId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var analyzedContractIdSet = documents
            .Where(d => d.ProcessingStatus == DocumentProcessingStatus.Completed && d.ContractId is not null)
            .Select(d => d.ContractId!.Value)
            .ToHashSet();

        var contracts = await dbContext.Contracts
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Select(c => new { c.Id, c.Currency, c.AnnualSpend })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshots = contracts.Select(c => new ContractAnalysisSnapshot(
            c.Id.Value, c.Currency, c.AnnualSpend, analyzedContractIdSet.Contains(c.Id)));

        return analysisCalculator.Summarize(snapshots);
    }
}
