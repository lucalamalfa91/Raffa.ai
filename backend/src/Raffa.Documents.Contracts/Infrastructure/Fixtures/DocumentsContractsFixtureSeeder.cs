using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Facts;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Infrastructure.Fixtures;

/// <summary>
/// AskRaffa 4.0 fixture seeder for contracts (fixture mode only). Scenario <c>full</c>: the
/// contracts the prototype starts with as validated (<c>validated:[2,3,4]</c> = Microsoft, AWS,
/// DocuSign) become real <c>contract</c> + <c>document</c> rows (document <c>Completed</c>) and a
/// validated <c>contract_facts</c> profile. Scenario <c>zero</c>: nothing — Salesforce arrives
/// through the upload + review flow, whose fixture extraction can build its facts with
/// <see cref="BuildFacts"/>. Contract ids are deterministic (<see cref="FixtureIds.Contract"/>).
/// </summary>
public sealed class DocumentsContractsFixtureSeeder(DocumentsContractsDbContext dbContext, ITenantContext tenantContext)
    : IFixtureScenarioSeeder
{
    public const string ContractsFile = "contracts.json";

    public int Order => FixtureSeedOrder.DocumentsContracts;

    public Task WipeAsync(FixtureSeedContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(FixtureSeedContext context, string scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var file = context.ReadJson<FixtureContractsFile>(ContractsFile);
        if (!file.ValidatedInScenario.TryGetValue(scenario, out var validated) || validated.Count == 0)
        {
            return;
        }

        using var _ = tenantContext.BeginScope(context.TenantId);
        foreach (var fixtureId in validated)
        {
            var facts = BuildFacts(context, file, fixtureId) with { ValidatedAt = context.Now };
            AddContract(context, facts);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The facts of fixture contract <paramref name="fixtureId"/> (1..4), with its real
    /// contract id, ISO dates, the evidence it cites and its review fields; not validated.</summary>
    public static ContractFacts BuildFacts(FixtureSeedContext context, int fixtureId)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BuildFacts(context, context.ReadJson<FixtureContractsFile>(ContractsFile), fixtureId);
    }

    private static ContractFacts BuildFacts(FixtureSeedContext context, FixtureContractsFile file, int fixtureId)
    {
        var raw = file.Contracts.SingleOrDefault(c => c.FixtureId == fixtureId)
            ?? throw new ArgumentOutOfRangeException(nameof(fixtureId), $"No fixture contract {fixtureId} in {ContractsFile}.");

        var keys = raw.Deep.Push.Concat(raw.Deep.Raise)
            .Select(p => p.EvidenceKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var evidence = file.Evidence
            .Where(e => keys.Contains(e.Key) || string.Equals(e.Value.Doc, raw.File, StringComparison.Ordinal))
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        // The flagged fields are those of the sample document (Salesforce_MSA_2024.pdf).
        var reviewFields = file.WeakFields
            .Where(f => file.Evidence.TryGetValue(f.Ev, out var ev) && ev.Doc == raw.File)
            .ToList();

        return raw with
        {
            ContractId = FixtureIds.Contract(context.TenantId, fixtureId).Value,
            StartDate = ParseDate(raw.Start),
            EndDate = ParseDate(raw.End),
            NoticeDeadline = ParseDate(raw.Cancel),
            Evidence = evidence,
            ReviewFields = reviewFields,
            ValidatedAt = null,
        };
    }

    /// <summary>Writes the contract, its (already read) document and its facts row.</summary>
    private void AddContract(FixtureSeedContext context, ContractFacts facts)
    {
        var contractId = new EntityId(facts.ContractId);
        var type = facts.Type switch
        {
            "MSA" => ContractDocumentType.Msa,
            "Amendment" => ContractDocumentType.Amendment,
            "Order Form" => ContractDocumentType.OrderForm,
            _ => ContractDocumentType.Other,
        };
        var renewalTerm = facts.Deep.Terms.FirstOrDefault(t => t.Label == "Renewal term")?.Value;
        int? renewalMonths = renewalTerm is not null && int.TryParse(renewalTerm.Split(' ')[0], out var months) ? months : null;

        dbContext.Contracts.Add(new Contract
        {
            Id = contractId,
            TenantId = context.TenantId,
            Type = type,
            Status = ExtractionConfidencePolicy.StatusActive,
            Currency = "CHF",
            StartDate = facts.StartDate,
            EndDate = facts.EndDate,
            EffectiveDate = facts.StartDate,
            CancellationDeadline = facts.NoticeDeadline,
            AnnualSpend = facts.Spend,
            AutoRenewal = facts.Auto == "Yes",
            RenewalTermMonths = renewalMonths,
            NoticePeriodDays = facts.Notice,
            PaymentTerms = facts.Deep.Terms.FirstOrDefault(t => t.Label == "Payment terms")?.Value,
            GoverningLaw = facts.Deep.Terms.FirstOrDefault(t => t.Label == "Governing law")?.Value,
            CreatedAt = context.Now,
        });

        var documentId = context.Id("document", facts.FixtureId ?? 0);
        dbContext.Documents.Add(new Document
        {
            Id = documentId,
            TenantId = context.TenantId,
            ContractId = contractId,
            FileName = facts.File,
            MimeType = "application/pdf",
            DocumentType = type,
            StoragePath = DocumentStoragePath.Build(context.TenantId, documentId, 1, facts.File),
            Checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("askraffa4:" + facts.File))),
            ProcessingStatus = DocumentProcessingStatus.Completed,
            CreatedAt = context.Now,
        });

        dbContext.ContractFacts.Add(new ContractFactsRecord
        {
            Id = context.Id("contract-facts", facts.FixtureId ?? 0),
            TenantId = context.TenantId,
            ContractId = contractId,
            Facts = ContractFactsReader.Serialize(facts),
            ValidatedAt = facts.ValidatedAt,
            UpdatedAt = context.Now,
        });
    }

    /// <summary>"18 Oct 2026" → 2026-10-18.</summary>
    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary><c>backend/fixtures/askraffa4/contracts.json</c>.</summary>
    public sealed record FixtureContractsFile
    {
        public Dictionary<string, List<int>> ValidatedInScenario { get; init; } = [];
        public List<ContractFacts> Contracts { get; init; } = [];
        public Dictionary<string, ContractEvidence> Evidence { get; init; } = [];
        public List<ContractReviewField> WeakFields { get; init; } = [];
    }
}

public static class DocumentsContractsFixtureServiceCollectionExtensions
{
    /// <summary>Registers the contracts fixture seeder (call only when fixtures are enabled).</summary>
    public static IServiceCollection AddDocumentsContractsFixtureSeeder(this IServiceCollection services)
    {
        services.AddScoped<IFixtureScenarioSeeder, DocumentsContractsFixtureSeeder>();
        return services;
    }
}
