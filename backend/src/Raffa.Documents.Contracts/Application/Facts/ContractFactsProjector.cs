using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Suppliers;

namespace Raffa.Documents.Contracts.Application.Facts;

/// <summary>
/// Builds the <see cref="ContractFacts"/> of a real (non-fixture) contract from its validated fields,
/// line items, obligations and clauses. One builder for every path that makes a contract validated:
/// the v4 review (<c>DocumentReadingService</c>) and <see cref="ContractFactsReader"/>, which projects
/// the facts of a contract whose document reached <c>Completed</c> another way (a clean extraction,
/// the nothing-to-review sweep, the legacy "Mark as validated"). Push/raise points and deep risks need
/// an analysis the pipeline does not produce yet, so they stay empty (no invented content). The caller
/// opens the tenant scope.
/// </summary>
public sealed class ContractFactsProjector(
    DocumentsContractsDbContext dbContext,
    IClock clock,
    ISupplierNameLookup? supplierNameLookup = null)
{
    public async Task<ContractFacts> BuildAsync(
        TenantId tenantId, EntityId contractId, Document document, CancellationToken cancellationToken = default)
    {
        var contract = await dbContext.Contracts.AsNoTracking()
            .SingleAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);
        // In the document's order (page, then extraction order): the first recurring line is the one the
        // renewal case prices, so the order must not depend on how the database returns rows.
        var lines = (await dbContext.ContractLineItems.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ContractId == contractId)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(l => l.SourcePage ?? int.MaxValue)
            .ThenBy(l => l.CreatedAt)
            .ThenBy(l => l.Id.Value)
            .ToList();
        var obligations = await dbContext.Obligations.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ContractId == contractId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var clauses = await dbContext.Clauses.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ContractId == contractId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var supplier = "";
        if (contract.SupplierId is { } supplierId && supplierNameLookup is not null)
        {
            var names = await supplierNameLookup.GetNamesAsync(tenantId, [supplierId], cancellationToken).ConfigureAwait(false);
            supplier = names.GetValueOrDefault(supplierId) ?? "";
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var deadline = contract.CancellationDeadline
            ?? (contract.EndDate is { } end && contract.NoticePeriodDays is { } notice ? end.AddDays(-notice) : null);
        string Date(DateOnly? d) => d is { } v ? ContractFactsReader.FormatDate(v) : "";
        string Money(decimal? v) => v is { } m ? contract.Currency.Trim().ToUpperInvariant() + " " + m.ToString("#,0", CultureInfo.InvariantCulture) : "";
        var terms = new List<ContractTerm>();
        void Term(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                terms.Add(new ContractTerm(label, value));
            }
        }

        Term("Annual spend", Money(contract.AnnualSpend));
        Term("Total contract value", Money(contract.TotalContractValue));
        Term("Term", contract.StartDate is not null || contract.EndDate is not null ? $"{Date(contract.StartDate)} → {Date(contract.EndDate)}" : null);
        Term("Renewal term", contract.RenewalTermMonths is { } months ? $"{months} months{(contract.AutoRenewal ? ", automatic" : "")}" : null);
        Term("Notice period", contract.NoticePeriodDays is { } days ? $"{days} days" : null);
        Term("Payment terms", contract.PaymentTerms);
        Term("Governing law", contract.GoverningLaw);

        static bool IsOurs(string party) =>
            party.Contains("customer", StringComparison.OrdinalIgnoreCase) || party.Contains("buyer", StringComparison.OrdinalIgnoreCase)
            || party.Contains("client", StringComparison.OrdinalIgnoreCase);
        ContractObligation Obligation(Obligation o) => new(
            o.Description,
            o.DueDate is { } due ? Date(due) : (o.RecurrenceRule ?? "—"),
            string.Equals(o.Criticality, "high", StringComparison.OrdinalIgnoreCase) ? "High" : (o.Criticality ?? "Medium"),
            o.RecurrenceRule ?? "once");

        return new ContractFacts
        {
            ContractId = contractId.Value,
            Supplier = supplier,
            Name = ContractDisplayName.Of(supplier, contract.Type.ToString()),
            Type = contract.Type.ToString(),
            File = document.FileName,
            Spend = contract.AnnualSpend ?? 0,
            Currency = contract.Currency.Trim().ToUpperInvariant(),
            Start = Date(contract.StartDate ?? contract.EffectiveDate),
            End = Date(contract.EndDate),
            Days = contract.EndDate is { } e ? e.DayNumber - today.DayNumber : 0,
            Cancel = Date(deadline),
            CancelDays = deadline is { } dl ? dl.DayNumber - today.DayNumber : 0,
            Notice = contract.NoticePeriodDays ?? 0,
            Auto = contract.AutoRenewal ? "Yes" : "No",
            RenewalTermMonths = contract.RenewalTermMonths,
            Uplift = "",
            StartDate = contract.StartDate ?? contract.EffectiveDate,
            EndDate = contract.EndDate,
            NoticeDeadline = deadline,
            DocCount = 1,
            Deep = new ContractDeepFacts
            {
                Products = lines.Select(l => new ContractProduct(
                    l.Description, l.Sku ?? "", l.Unit ?? "", l.Quantity ?? 1, l.UnitPrice ?? 0, l.AnnualCost ?? 0, l.BillingPeriod)).ToList(),
                Std = clauses.Select(c => new ContractStandardTerm(
                    c.ClauseType, c.NormalizedValue ?? c.RawText, c.SourcePage is { } p ? $"p.{p}" : "")).ToList(),
                OblYou = obligations.Where(o => IsOurs(o.Party)).Select(Obligation).ToList(),
                OblSup = obligations.Where(o => !IsOurs(o.Party)).Select(Obligation).ToList(),
                Terms = terms,
            },
        };
    }
}
