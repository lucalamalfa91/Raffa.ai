using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Raffa.Quotes.Application.AssessmentV4;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Quotes.Infrastructure.Fixtures;

/// <summary>
/// AskRaffa 4.0 fixture seeder for Offerta (order <see cref="FixtureSeedOrder.Quotes"/>): the saved
/// quote checks of <c>SQ.QUOTES</c> (<c>quotes.json</c>) — Databricks (to decide) and Snowflake
/// (signed) — in scenario <c>full</c> only, as the prototype's <c>allQuotes()</c>.
/// </summary>
public sealed class QuotesFixtureSeeder(QuotesDbContext dbContext, ITenantContext tenantContext) : IFixtureScenarioSeeder
{
    public const string QuotesFile = "quotes.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public int Order => FixtureSeedOrder.Quotes;

    public Task WipeAsync(FixtureSeedContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(FixtureSeedContext context, string scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (scenario != FixtureScenarios.Full)
        {
            return;
        }

        var file = context.ReadJson<FixtureQuotesFile>(QuotesFile);
        using var _ = tenantContext.BeginScope(context.TenantId);
        foreach (var raw in file.Quotes)
        {
            var quoteId = context.Id("quote", raw.Id);
            var date = QuoteFormat.Pd(raw.Date);
            var created = date is { } d ? new DateTimeOffset(d.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero) : context.Now;
            var decided = QuoteFormat.Pd(raw.DecidedAt);
            dbContext.Quotes.Add(new Quote
            {
                Id = quoteId,
                TenantId = context.TenantId,
                FileName = raw.File,
                MimeType = "application/pdf",
                StoragePath = $"fixtures/{context.TenantId.Value}/quotes/{raw.Id}/{raw.File}",
                Checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("askraffa4:" + raw.File))),
                ProcessingStatus = QuoteProcessingStatus.Completed,
                Supplier = raw.Supplier,
                Currency = raw.Currency,
                CreatedAt = created,
                Kind = raw.Kind,
                ContractId = raw.Cid is { } cid ? FixtureIds.Contract(context.TenantId, cid) : null,
                Status = raw.Status,
                DecidedBy = decided is null ? null : raw.By,
                DecidedAt = decided is { } dd ? new DateTimeOffset(dd.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero) : null,
                ValidUntil = QuoteFormat.Pd(raw.Valid),
                Reference = raw.Ref,
                TermLabel = raw.Term,
                QuoteDate = date,
                CheckedBy = raw.By,
                ExtractionConfirmedAt = raw.Status == QuoteDecisionStatus.Checked ? null : created,
                StatedTermsJson = raw.Stated?.GetRawText(),
                MissingJson = JsonSerializer.Serialize(raw.Gaps, Json),
                TargetJson = raw.Target is null ? null : JsonSerializer.Serialize(raw.Target, Json),
            });

            var i = 0;
            foreach (var l in raw.Lines)
            {
                dbContext.QuoteLines.Add(new QuoteLine
                {
                    Id = context.Id("quote-line", $"{raw.Id}:{i}"),
                    TenantId = context.TenantId,
                    QuoteId = quoteId,
                    Sku = l.Sku,
                    Description = l.Name,
                    Quantity = l.Qty,
                    Unit = l.Unit,
                    UnitPrice = l.Price,
                    ExtendedPrice = l.Price * l.Qty,
                    Term = raw.Term,
                    Confidence = 1.0,
                    ExistingUnitPrice = l.Existing,
                    ExistingPriceLabel = l.ExistingLabel,
                    CreatedAt = created.AddSeconds(i),
                });
                i++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public sealed record FixtureQuotesFile
    {
        public List<FixtureQuote> Quotes { get; init; } = [];
    }

    public sealed record FixtureQuote
    {
        public string Id { get; init; } = "";
        public string Ref { get; init; } = "";
        public string Supplier { get; init; } = "";
        public string File { get; init; } = "";
        public string Kind { get; init; } = "new";
        public int? Cid { get; init; }
        public string? Date { get; init; }
        public string? By { get; init; }
        public string Currency { get; init; } = "CHF";
        public string? Term { get; init; }
        public string? Valid { get; init; }
        public string Status { get; init; } = QuoteDecisionStatus.Checked;
        public string? DecidedAt { get; init; }
        public OfferTarget? Target { get; init; }
        public List<FixtureQuoteLine> Lines { get; init; } = [];
        public JsonElement? Stated { get; init; }
        public List<BiText> Gaps { get; init; } = [];
    }

    public sealed record FixtureQuoteLine
    {
        public string Name { get; init; } = "";
        public string? Sku { get; init; }
        public decimal Qty { get; init; }
        public string Unit { get; init; } = "";
        public decimal Price { get; init; }
        public decimal? Existing { get; init; }
        public string? ExistingLabel { get; init; }
    }
}

public static class QuotesFixtureServiceCollectionExtensions
{
    /// <summary>Registers the Offerta fixture seeder (call only when fixtures are enabled).</summary>
    public static IServiceCollection AddQuotesFixtureSeeder(this IServiceCollection services)
    {
        services.AddScoped<IFixtureScenarioSeeder, QuotesFixtureSeeder>();
        return services;
    }
}
