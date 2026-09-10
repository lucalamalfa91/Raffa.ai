using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Infrastructure;

/// <summary>
/// EF Core DbContext for the Quotes bounded context (ADR-003; task E05/F01/US01/T01,
/// quote-extraction — this module's first <c>DbContext</c>). Postgres via npgsql is the only
/// access path; schema changes flow through code-first migrations only (no hand-edited DDL). RLS
/// policies and the ambient per-request tenant claim are wired the same way
/// <c>Raffa.Savings.Infrastructure.SavingsDbContext</c> /
/// <c>Raffa.Renewals.Infrastructure.RenewalsDbContext</c> already wire them — this context only
/// shapes the model and exposes the DbSets.
/// </summary>
public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<QuoteExtractionJob> QuoteExtractionJobs => Set<QuoteExtractionJob>();

    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();

    /// <summary>Task E05/F01/US02/T01 (sku-normalization) — see <see cref="SkuProductMapping"/>'s
    /// own doc comment.</summary>
    public DbSet<SkuProductMapping> SkuProductMappings => Set<SkuProductMapping>();

    /// <summary>Task E05/F03/US02/T01 (negotiation-outcome) — see <see cref="NegotiationOutcome"/>'s
    /// own doc comment.</summary>
    public DbSet<NegotiationOutcome> NegotiationOutcomes => Set<NegotiationOutcome>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new QuoteConfiguration());
        modelBuilder.ApplyConfiguration(new QuoteExtractionJobConfiguration());
        modelBuilder.ApplyConfiguration(new QuoteLineConfiguration());
        modelBuilder.ApplyConfiguration(new SkuProductMappingConfiguration());
        modelBuilder.ApplyConfiguration(new NegotiationOutcomeConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
