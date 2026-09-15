using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

/// <summary>
/// EF configuration for <see cref="ContractNegotiationStep"/> (task E19/F03/US01/T01, NW-13;
/// ADR-028 §D3). Table <c>contract_negotiation_step</c>; its Row-Level Security policy ships in
/// this table's own migration (ADR-009 w16 clause 2), the same "table and policy together, no
/// window where the table exists unprotected" treatment
/// <c>Raffa.Identity.Workspace.Migrations.AddWorkspaceInvitation</c> already gives
/// <c>workspace_invitation</c>.
///
/// <see cref="ContractNegotiationStep.Step"/> is stored **by name** as <c>varchar(60)</c> — the
/// same enum-as-string convention <c>Raffa.Quotes.Domain.NegotiationOutcome.LeversUsed</c> already
/// uses for its own closed vocabulary, simplified to a single value rather than a list (closer in
/// shape to <c>Raffa.Renewals.Infrastructure.Configurations.RenewalActionConfiguration</c>'s own
/// single-value <c>Status</c> column). 60 characters comfortably covers the longest current member
/// (<c>SignOrSendNonRenewalNotice</c>, 27 characters) with room to spare, should ADR-001 w16 clause
/// 3's "a fifth is a product change" ever add one.
///
/// No foreign key to <c>contract</c>, even though both tables live in this module — see
/// <see cref="ContractNegotiationStep"/>'s own doc comment for why, and see
/// <see cref="Application.NegotiationStepService"/> for the explicit existence check that stands in
/// for it.
/// </summary>
public sealed class ContractNegotiationStepConfiguration : IEntityTypeConfiguration<ContractNegotiationStep>
{
    public void Configure(EntityTypeBuilder<ContractNegotiationStep> builder)
    {
        builder.ToTable("contract_negotiation_step");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        // Enum-as-string (see the type doc comment) -- readable in `psql` without decoding an
        // ordinal, same convention RenewalActionConfiguration.Status already uses.
        builder.Property(e => e.Step).HasConversion<string>().HasMaxLength(60);

        builder.HasIndex(e => e.TenantId);

        // The row's presence is the tick (ContractNegotiationStep's own doc comment): at most one
        // row per step per contract per tenant, so NegotiationStepService's whole-set write can
        // insert missing rows and delete absent ones with no risk of ever duplicating a tick. This
        // unique index is the database-level backstop, the same role
        // RenewalActionConfiguration's own (tenant_id, contract_id) unique index plays for its
        // single-row upsert -- one level deeper here, since one contract carries up to four rows
        // instead of one.
        builder.HasIndex(e => new { e.TenantId, e.ContractId, e.Step }).IsUnique();
    }
}
