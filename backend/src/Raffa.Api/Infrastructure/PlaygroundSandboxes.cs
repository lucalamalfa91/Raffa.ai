using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Domain;
using Raffa.Suppliers.Products.Infrastructure;

namespace Raffa.Api.Infrastructure;

/// <summary>The three synthetic workspaces of the Super Admin playground (docs/architecture/admin-agent-playground-plan.md).</summary>
internal static class PlaygroundSandboxKinds
{
    public const string Empty = "empty";
    public const string Seeded = "seeded";
    public const string NeedsReview = "needs-review";

    public static IReadOnlyList<string> All { get; } = [Empty, Seeded, NeedsReview];

    /// <summary>Fixed tenant ids (never random: the sandboxes are the same in every environment).</summary>
    public static TenantId? TenantOf(string? kind) => kind switch
    {
        Empty => new TenantId(new Guid("d0000000-0000-4000-8000-000000000001")),
        Seeded => new TenantId(new Guid("d0000000-0000-4000-8000-000000000002")),
        NeedsReview => new TenantId(new Guid("d0000000-0000-4000-8000-000000000003")),
        _ => null,
    };

    public static string Describe(string kind) => kind switch
    {
        Empty => "Empty workspace: no contracts, no suppliers. Ask has nothing validated to answer from.",
        Seeded => "Four validated contracts (Salesforce MSA, Microsoft, AWS amendment, DocuSign order form), all CHF.",
        NeedsReview => "One Salesforce MSA still in needs_review: end date, notice deadline and annual spend not accepted yet.",
        _ => string.Empty,
    };
}

/// <summary>
/// Creates the playground sandbox workspaces on first use and keeps them as defined here: registers each
/// tenant as a sandbox in the platform registry and inserts its synthetic suppliers, contracts and linked
/// documents under that tenant's own RLS scope (no exception is added). Idempotent: rows that exist are left
/// alone. The data mirrors the golden-set tenant fixtures of <c>Raffa.AiEval</c>, so a question tried here
/// reads the same portfolio the eval runs on.
/// </summary>
internal sealed class PlaygroundSandboxSeeder(
    PlaygroundStore store,
    SuppliersDbContext suppliers,
    DocumentsContractsDbContext documents,
    ITenantContext tenantContext,
    IClock clock)
{
    private static readonly DateTimeOffset SeedInstant = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    public async Task EnsureAsync(string kind, CancellationToken ct)
    {
        var tenantId = PlaygroundSandboxKinds.TenantOf(kind)
            ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sandbox kind.");

        await store.RegisterSandboxAsync(tenantId.Value, $"Playground · {kind}", clock.UtcNow, ct).ConfigureAwait(false);

        var (supplierRows, contractRows) = Build(kind, tenantId);
        if (contractRows.Count == 0)
        {
            return;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        foreach (var supplier in supplierRows)
        {
            if (!await suppliers.Suppliers.AnyAsync(s => s.Id == supplier.Id, ct).ConfigureAwait(false))
            {
                suppliers.Suppliers.Add(supplier);
            }
        }

        await suppliers.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var contract in contractRows)
        {
            if (await documents.Contracts.AnyAsync(c => c.Id == contract.Id, ct).ConfigureAwait(false))
            {
                continue;
            }

            documents.Contracts.Add(contract);
            // The portfolio hides a contract with no linked document: give each one a document whose status
            // matches the contract's own, as the eval host does.
            documents.Documents.Add(new Document
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                FileName = $"{contract.Id.Value}.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value}/{contract.Id.Value}.pdf",
                Checksum = $"sha256:{contract.Id.Value:N}",
                ProcessingStatus = contract.Status.Contains("review", StringComparison.OrdinalIgnoreCase)
                    ? DocumentProcessingStatus.NeedsReview
                    : DocumentProcessingStatus.Completed,
                CreatedAt = contract.CreatedAt,
            });
        }

        await documents.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountContractsAsync(string kind, CancellationToken ct)
    {
        var tenantId = PlaygroundSandboxKinds.TenantOf(kind);
        if (tenantId is null)
        {
            return 0;
        }

        using var scope = tenantContext.BeginScope(tenantId.Value);
        // Explicit tenant filter: RLS does it in Postgres, the in-memory test store does not.
        return await documents.Contracts.CountAsync(c => c.TenantId == tenantId.Value, ct).ConfigureAwait(false);
    }

    private static (List<Supplier> Suppliers, List<Contract> Contracts) Build(string kind, TenantId tenantId)
    {
        switch (kind)
        {
            case PlaygroundSandboxKinds.Seeded:
            {
                var salesforce = NewSupplier(tenantId, "e0000000-0000-4000-8000-000000000001", "Salesforce");
                var microsoft = NewSupplier(tenantId, "e0000000-0000-4000-8000-000000000002", "Microsoft");
                var aws = NewSupplier(tenantId, "e0000000-0000-4000-8000-000000000003", "AWS");
                var docuSign = NewSupplier(tenantId, "e0000000-0000-4000-8000-000000000004", "DocuSign");
                return (
                    [salesforce, microsoft, aws, docuSign],
                    [
                        NewContract(tenantId, "f0000000-0000-4000-8000-000000000001", salesforce.Id, ContractDocumentType.Msa, "Completed",
                            new DateOnly(2024, 3, 1), new DateOnly(2027, 1, 15), new DateOnly(2026, 10, 18), 640_000m, true, SeedInstant),
                        NewContract(tenantId, "f0000000-0000-4000-8000-000000000002", microsoft.Id, ContractDocumentType.Msa, "Completed",
                            new DateOnly(2024, 7, 1), new DateOnly(2027, 6, 30), new DateOnly(2027, 3, 31), 1_200_000m, true, SeedInstant.AddSeconds(-1)),
                        NewContract(tenantId, "f0000000-0000-4000-8000-000000000003", aws.Id, ContractDocumentType.Amendment, "Completed",
                            new DateOnly(2024, 9, 1), new DateOnly(2027, 8, 31), new DateOnly(2027, 6, 1), 2_400_000m, false, SeedInstant.AddSeconds(-2)),
                        NewContract(tenantId, "f0000000-0000-4000-8000-000000000004", docuSign.Id, ContractDocumentType.OrderForm, "Completed",
                            new DateOnly(2023, 11, 1), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 2), 58_000m, true, SeedInstant.AddSeconds(-3)),
                    ]);
            }

            case PlaygroundSandboxKinds.NeedsReview:
            {
                var salesforce = NewSupplier(tenantId, "e0000000-0000-4000-8000-000000000011", "Salesforce");
                return (
                    [salesforce],
                    [NewContract(tenantId, "f0000000-0000-4000-8000-000000000011", salesforce.Id, ContractDocumentType.Msa, "needs_review",
                        new DateOnly(2024, 3, 1), null, null, null, true, SeedInstant)]);
            }

            default:
                return ([], []);
        }
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
        TenantId tenantId, string id, EntityId supplierId, ContractDocumentType type, string status,
        DateOnly? start, DateOnly? end, DateOnly? cancellationDeadline, decimal? annualSpend, bool autoRenewal, DateTimeOffset createdAt) => new()
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

/// <summary>Per-million-token prices (USD) of a Foundry deployment, from the Azure retail price list.</summary>
internal sealed class PlaygroundModelPrice
{
    public decimal InputPer1M { get; set; }
    public decimal CachedInputPer1M { get; set; }
    public decimal OutputPer1M { get; set; }
}

/// <summary><c>Playground:Pricing:Models:&lt;deployment prefix&gt;</c>: a deployment (e.g. <c>gpt-5.4-nano-dev</c>) takes the
/// price of the longest configured key it starts with. A deployment with no price shows no cost, never zero.</summary>
internal sealed class PlaygroundPricingOptions
{
    public const string SectionName = "Playground:Pricing";

    public Dictionary<string, PlaygroundModelPrice> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public decimal? CostUsd(string? deployment, int promptTokens, int completionTokens, int cachedTokens)
    {
        if (string.IsNullOrWhiteSpace(deployment))
        {
            return null;
        }

        var key = Models.Keys
            .Where(k => deployment.StartsWith(k, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        if (key is null)
        {
            return null;
        }

        var price = Models[key];
        var cached = Math.Min(cachedTokens, promptTokens);
        return ((promptTokens - cached) * price.InputPer1M + cached * price.CachedInputPer1M + completionTokens * price.OutputPer1M) / 1_000_000m;
    }
}
