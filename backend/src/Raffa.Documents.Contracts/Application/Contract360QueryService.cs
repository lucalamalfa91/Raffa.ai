using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Implements task E02/F03/US02/T01 (us-02-contract-360-aggregate, AC-1/AC-2/AC-3): `GET
/// /api/contracts/{id}` — the header + tab aggregate product spec §8.2 names (see
/// <see cref="Contract360Result"/> for the full field-by-field rationale), scoped to the caller's
/// tenant (ADR-009).
///
/// Same shape as <see cref="DocumentQueryService"/>/<see cref="ContractCorrectionService"/>:
/// nothing upstream opens a tenant scope before a read runs (see <c>Raffa.Api.Program</c>), so
/// this opens its own <see cref="ITenantContext.BeginScope"/> rather than trusting one is already
/// active, and every query below also filters explicitly by <see cref="TenantId"/> on top of that
/// scope — belt-and-suspenders on top of the Postgres RLS backstop (ADR-009), so a cross-tenant
/// contract id reads back as "not found" for two independent reasons, not one.
///
/// Every child collection (line items/clauses/obligations/risks/documents) is queried
/// independently, sequentially, against this same <see cref="DocumentsContractsDbContext"/>
/// instance — never <c>Task.WhenAll</c>'d, since a single EF Core <c>DbContext</c> is not safe for
/// concurrent operations.
/// </summary>
public sealed class Contract360QueryService(DocumentsContractsDbContext dbContext, ITenantContext tenantContext)
{
    public async Task<Contract360Result?> GetByIdAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        // Entry point: open this call's own tenant scope (see the type doc comment) before any
        // query below, since the RLS connection interceptor reads ITenantContext.Current only
        // when the connection opens, which EF Core does lazily on first use.
        using var _ = tenantContext.BeginScope(tenantId);

        var contract = await dbContext.Contracts
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return null;
        }

        var lineItems = await dbContext.ContractLineItems
            .AsNoTracking()
            .Where(li => li.TenantId == tenantId && li.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var clauses = await dbContext.Clauses
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var obligations = await dbContext.Obligations
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var risks = await dbContext.Risks
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var documents = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Same "highest recorded severity" rule as PortfolioQueryService's own Risk column — an
        // in-memory Max over RiskSeverity's declared enum order (Low < Medium < High < Critical),
        // not its HasConversion<string>() column representation, since these lists are already
        // materialized above.
        RiskSeverity? topRisk = risks.Count == 0 ? null : risks.Max(r => r.Severity);

        // "Renewal" is derived, never stored — see Contract360Header's own doc comment.
        DateOnly? renewalDate = contract.AutoRenewal ? contract.EndDate : null;

        var header = new Contract360Header(
            contract.Id,
            contract.SupplierId,
            contract.Type,
            contract.Status,
            contract.AnnualSpend,
            contract.TotalContractValue,
            contract.StartDate,
            contract.EndDate,
            renewalDate,
            contract.CancellationDeadline,
            contract.AutoRenewal,
            topRisk);

        var overview = new Contract360Overview(
            contract.Currency,
            contract.EffectiveDate,
            contract.RenewalTermMonths,
            contract.PaymentTerms,
            contract.GoverningLaw,
            contract.ParentContractId,
            contract.Version,
            contract.CreatedAt);

        var commercials = new Contract360Commercials(
            contract.AnnualSpend,
            contract.TotalContractValue,
            contract.Currency,
            contract.PaymentTerms,
            contract.AutoRenewal,
            contract.RenewalTermMonths,
            lineItems.Count,
            lineItems.Count == 0 ? null : lineItems.Sum(li => li.AnnualCost ?? 0m),
            lineItems.Count == 0 ? null : lineItems.Sum(li => li.TotalCost ?? 0m));

        var products = lineItems
            .Select(li => new Contract360ProductLineItem(
                li.Id,
                li.ProductId,
                li.Sku,
                li.Description,
                li.Quantity,
                li.Unit,
                li.UnitPrice,
                li.ListPrice,
                li.Discount,
                li.BillingPeriod,
                li.AnnualCost,
                li.TotalCost,
                li.SourceDocumentId,
                li.SourceSpan,
                li.SourcePage,
                li.Confidence))
            .ToList();

        var clauseRows = clauses
            .Select(c => new Contract360Clause(
                c.Id,
                c.ClauseType,
                c.RawText,
                c.NormalizedValue,
                c.RiskLevel,
                c.SourceDocumentId,
                c.SourceSpan,
                c.SourcePage,
                c.Confidence))
            .ToList();

        var obligationRows = obligations
            .Select(o => new Contract360Obligation(
                o.Id,
                o.Party,
                o.ObligationType,
                o.Description,
                o.DueDate,
                o.RecurrenceRule,
                o.Criticality,
                o.Status,
                o.SourceDocumentId,
                o.SourceSpan,
                o.SourcePage,
                o.Confidence))
            .ToList();

        var riskRows = risks
            .Select(r => new Contract360Risk(
                r.Id,
                r.RiskType,
                r.Severity,
                r.Description,
                r.Status,
                r.ClauseId,
                r.SourceDocumentId,
                r.SourceSpan,
                r.SourcePage,
                r.Confidence))
            .ToList();

        var documentRows = documents
            .Select(d => new Contract360Document(
                d.Id,
                d.FileName,
                d.MimeType,
                d.DocumentType,
                d.ProcessingStatus,
                d.CreatedAt))
            .ToList();

        var renewal = new Contract360Renewal(
            contract.EndDate,
            renewalDate,
            contract.CancellationDeadline,
            contract.AutoRenewal,
            contract.RenewalTermMonths);

        return new Contract360Result(
            contract.Id,
            header,
            overview,
            commercials,
            products,
            clauseRows,
            obligationRows,
            riskRows,
            documentRows,
            Array.Empty<Contract360BenchmarkEntry>(),
            renewal,
            Array.Empty<Contract360ActivityEntry>());
    }
}
