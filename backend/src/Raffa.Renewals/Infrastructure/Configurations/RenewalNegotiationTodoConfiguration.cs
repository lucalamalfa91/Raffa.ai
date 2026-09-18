using Raffa.Renewals.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Raffa.Renewals.Infrastructure.Configurations;

/// <summary>
/// EF Core mapping for <see cref="RenewalNegotiationTodo"/> (task E29/F01/US01/T01,
/// todo-entity-api). Same shape as <see cref="RenewalActionConfiguration"/>/
/// <see cref="RenewalAlertConfiguration"/> (enum-as-string columns, module-local
/// <see cref="ValueConverters"/>, a <see cref="TenantScopedEntity"/> id/tenant pair), plus one
/// addition: <see cref="RenewalNegotiationTodo.CitationKeys"/> as a comma-separated string column —
/// the same convention <c>Raffa.Quotes.Infrastructure.Configurations.NegotiationOutcomeConfiguration</c>
/// already uses for <c>NegotiationOutcome.LeversUsed</c> ("this module has no precedent for a
/// native Postgres array column... just extended to a list"), here extended one step further to a
/// list of free-form citation-key strings rather than enum names.
/// </summary>
public sealed class RenewalNegotiationTodoConfiguration : IEntityTypeConfiguration<RenewalNegotiationTodo>
{
    /// <summary>
    /// <see cref="RenewalNegotiationTodo.CitationKeys"/> as a comma-separated list (e.g.
    /// <c>"fact:11111111-...:priced-line[0].unitPrice,market:mkt-42"</c>). Citation keys are built
    /// exclusively by <c>Raffa.Insights.Contracts.InsightsCitationKeys</c>'s three fixed shapes
    /// (<c>fact:</c>/<c>market:</c>/<c>calc:</c> prefixes, each followed by a colon/bracket-shaped
    /// suffix, never caller-typed free text), so none can ever legitimately contain a comma — the
    /// same "closed shape, so the simple separator is safe" reasoning
    /// <c>NegotiationOutcomeConfiguration.LeversUsedConverter</c>'s own doc comment gives for reusing
    /// a bare <c>,</c> instead of a JSON array column.
    /// </summary>
    private static readonly ValueConverter<IReadOnlyList<string>, string> CitationKeysConverter = new(
        keys => string.Join(',', keys),
        value => string.IsNullOrEmpty(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries));

    public void Configure(EntityTypeBuilder<RenewalNegotiationTodo> builder)
    {
        builder.ToTable("renewal_negotiation_todo");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        // PointKey/Topic: closed-vocabulary-shaped strings (see the entity's own doc comment) --
        // generous headroom, not a real bound, same convention RenewalAlertConfiguration.Milestone
        // already uses for its own closed-vocabulary column.
        builder.Property(e => e.PointKey).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Topic).IsRequired().HasMaxLength(200);

        // Current/Target/Rationale: deliberately no HasMaxLength -> Postgres `text` (unbounded
        // free-form record), same convention RenewalAction.Action/AuditEvent.Detail already use for
        // their own unbounded-by-nature fields.
        builder.Property(e => e.Current).IsRequired();
        builder.Property(e => e.Target).IsRequired();
        builder.Property(e => e.Rationale).IsRequired();

        builder.Property(e => e.CitationKeys)
            .HasConversion(CitationKeysConverter)
            .IsRequired();

        builder.Property(e => e.Source).IsRequired().HasMaxLength(20);

        // Same enum-as-string convention as RenewalActionConfiguration.Status/
        // RenewalAlertConfiguration.Status (readable in `psql` without decoding an integer).
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.TenantId);

        // The entity's own key triple (RenewalNegotiationTodo's own doc comment; parent story AC-1):
        // at most one row per (tenant, contract, pointKey) -- the database-level backstop for
        // RenewalNegotiationTodoService.UpsertAsync's own check-then-act reconciliation, same
        // "two independent reasons, not one" belt-and-suspenders shape RenewalActionConfiguration's
        // own unique index already documents.
        builder.HasIndex(e => new { e.TenantId, e.ContractId, e.PointKey })
            .IsUnique()
            .HasDatabaseName("ix_renewal_negotiation_todo_tenant_contract_point_key");
    }
}
