using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Raffa.Savings.Application.Results;
using Raffa.Savings.Domain.Results;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Savings.Infrastructure.Fixtures;

/// <summary>
/// AskRaffa 4.0 fixture seeder for Risultati (fixture mode only). Scenario <c>full</c>: the signed
/// results of <c>SQ.HIST</c> (<c>history.json → results</c>) become <c>saving_result</c> rows and
/// <c>history.json → invoices</c> become <c>invoice</c> + <c>saving_realization</c> rows (realized =
/// what the invoices prove). Scenario <c>zero</c>: nothing — no signed result yet.
/// </summary>
public sealed partial class SavingsFixtureSeeder(SavingsDbContext dbContext, ITenantContext tenantContext) : IFixtureScenarioSeeder
{
    public const string HistoryFile = "history.json";

    public int Order => FixtureSeedOrder.Savings;

    public Task WipeAsync(FixtureSeedContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(FixtureSeedContext context, string scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (scenario != FixtureScenarios.Full) return;

        var file = context.ReadJson<HistoryFixtureFile>(HistoryFile);
        using var _ = tenantContext.BeginScope(context.TenantId);

        var ids = new Dictionary<string, EntityId>(StringComparer.Ordinal);
        foreach (var h in file.Results)
        {
            var id = context.Id("saving-result", h.Id);
            ids[h.Id] = id;
            var (from, to) = ParsePeriod(h.Period);
            var record = new SavingResultRecord(
                ContractId: null,
                CaseId: null,
                SupplierName: h.Supplier,
                ContractName: h.Contract,
                Category: h.Cat,
                Kind: h.Kind,
                AmountAnnual: h.Amount,
                Currency: "CHF",
                Note: h.Note,
                BaselineDescription: h.Baseline,
                BaselineValue: null,
                NewValue: null,
                Quantity: null,
                Method: h.Method,
                PeriodFrom: from,
                PeriodTo: to,
                PeriodDescription: h.Period,
                EvidenceDocumentId: null,
                EvidenceFileName: h.Evidence,
                OwnerName: h.Owner,
                SignedOn: ParseDay(h.When) ?? context.Today,
                RecordedBy: h.Owner);
            dbContext.SavingResults.Add(SavingResultRecorder.ToEntity(context.TenantId, record, context.Now, id));
        }

        var n = 0;
        foreach (var inv in file.Invoices)
        {
            if (!ids.TryGetValue(inv.ResultId, out var resultId)) continue;
            n++;
            var invoiceId = context.Id("invoice", $"{inv.ResultId}:{inv.Period}");
            dbContext.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                TenantId = context.TenantId,
                SupplierName = inv.Supplier,
                Period = DateOnly.ParseExact(inv.Period, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Amount = inv.Amount,
                Currency = "CHF",
                CreatedAt = context.Now,
            });
            dbContext.SavingRealizations.Add(new SavingRealization
            {
                Id = context.Id("saving-realization", n),
                TenantId = context.TenantId,
                SavingResultId = resultId,
                InvoiceId = invoiceId,
                RealizedAmount = inv.Realized,
                ComputedAt = context.Now,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>"12 months from 01 Apr 2026" → 2026-04-01 .. 2027-03-31; other forms: start only.</summary>
    public static (DateOnly? From, DateOnly? To) ParsePeriod(string text)
    {
        var m = PeriodPattern().Match(text ?? "");
        if (!m.Success) return (null, null);
        var from = ParseDay(m.Groups["from"].Value);
        if (from is null) return (null, null);
        return m.Groups["months"].Success
            ? (from, from.Value.AddMonths(int.Parse(m.Groups["months"].Value, CultureInfo.InvariantCulture)).AddDays(-1))
            : (from, null);
    }

    public static DateOnly? ParseDay(string? text) =>
        DateOnly.TryParseExact(text, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [GeneratedRegex(@"^(?:(?<months>\d+) months )?.*?from (?<from>\d{2} \w{3} \d{4})$")]
    private static partial Regex PeriodPattern();

    /// <summary><c>backend/fixtures/askraffa4/history.json</c>.</summary>
    public sealed record HistoryFixtureFile
    {
        public List<HistoryResult> Results { get; init; } = [];
        public List<HistoryInvoice> Invoices { get; init; } = [];
    }

    public sealed record HistoryResult(
        string Id, string Supplier, string Contract, string Cat, string Kind, string Lever, string Owner, decimal Amount,
        string When, string Evidence, string Note, string Baseline, string Period, string Method);

    public sealed record HistoryInvoice(string ResultId, string Supplier, string Period, decimal Amount, decimal Realized);
}

public static class SavingsFixtureServiceCollectionExtensions
{
    /// <summary>Registers the Risultati fixture seeder and the deterministic goal parser (fixture mode
    /// reads goals exactly like the prototype). Call only when fixtures are enabled, before the
    /// modules, so the parser wins over the LLM one AddAiFlows tries to add.</summary>
    public static IServiceCollection AddSavingsFixtureSeeder(this IServiceCollection services)
    {
        services.AddScoped<IFixtureScenarioSeeder, SavingsFixtureSeeder>();
        services.AddScoped<ISavingsGoalParser, DeterministicSavingsGoalParser>();
        return services;
    }
}
