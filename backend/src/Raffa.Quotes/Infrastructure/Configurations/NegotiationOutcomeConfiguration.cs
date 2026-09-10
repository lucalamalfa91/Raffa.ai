using Raffa.Quotes.Application.Strategy;
using Raffa.Quotes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Raffa.Quotes.Infrastructure.Configurations;

public sealed class NegotiationOutcomeConfiguration : IEntityTypeConfiguration<NegotiationOutcome>
{
    /// <summary>
    /// <see cref="NegotiationOutcome.LeversUsed"/> as a comma-separated list of
    /// <see cref="NegotiationLeverType"/> names (e.g. <c>"Term,QuarterEnd"</c>) — this module has no
    /// precedent for a native Postgres array column, and every other closed-vocabulary column here
    /// (<c>QuoteConfiguration.ProcessingStatus</c>, <c>QuoteLineConfiguration.MatchStatus</c>)
    /// already uses the simpler "enum as string" mapping, just extended to a list. Kept local to
    /// this configuration (not <see cref="ValueConverters"/>) since that shared class is scoped to
    /// <c>Raffa.SharedKernel</c>'s strongly-typed id wrappers, not module-local enum lists.
    /// </summary>
    private static readonly ValueConverter<IReadOnlyList<NegotiationLeverType>, string> LeversUsedConverter = new(
        levers => string.Join(',', levers.Select(l => l.ToString())),
        value => string.IsNullOrEmpty(value)
            ? Array.Empty<NegotiationLeverType>()
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(name => Enum.Parse<NegotiationLeverType>(name))
                .ToArray());

    public void Configure(EntityTypeBuilder<NegotiationOutcome> builder)
    {
        builder.ToTable("negotiation_outcome");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.QuoteId)
            .HasConversion(ValueConverters.EntityIdConverter);

        // Same precision as QuoteLineConfiguration's own UnitPrice/ListPrice/ExtendedPrice.
        builder.Property(e => e.OriginalQuoteTotal).HasPrecision(18, 2);
        builder.Property(e => e.TargetPrice).HasPrecision(18, 2);
        builder.Property(e => e.FinalPrice).HasPrecision(18, 2);
        builder.Property(e => e.RealizedSaving).HasPrecision(18, 2);
        // Same precision as QuoteLineConfiguration's own DiscountPercent.
        builder.Property(e => e.DiscountPercent).HasPrecision(9, 4);

        builder.Property(e => e.LeversUsed)
            .HasConversion(LeversUsedConverter)
            .HasMaxLength(200)
            .IsRequired();

        // Task E05/F03/US02/T02 (outcome-propagation): cross-module reference by id only,
        // deliberately no FK (ADR-002; see the entity's own doc comment) — same treatment
        // Raffa.Savings.Infrastructure.Configurations.SavingsOpportunityConfiguration already
        // gives SavingsOpportunity.ContractId/SupplierId.
        builder.Property(e => e.SavingsOpportunityId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.QuoteId });
    }
}
