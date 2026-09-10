using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <summary>
    /// Task E05/F03/US02/T02 (outcome-propagation): adds the nullable
    /// <c>savings_opportunity_id</c> column to the existing <c>negotiation_outcome</c> table — see
    /// <see cref="Raffa.Quotes.Domain.NegotiationOutcome.SavingsOpportunityId"/>'s own doc comment
    /// for why it is nullable and caller-supplied. No RLS change needed:
    /// <c>negotiation_outcome</c> already has row-level security from
    /// <c>AddTenantRowLevelSecurity</c>, which is a table/row-level policy that automatically covers
    /// new columns on the same table — mirrors <c>AddQuoteBenchmarkMatchFields</c>'s own identical
    /// "no new table" reasoning.
    /// </summary>
    public partial class AddNegotiationOutcomeSavingsOpportunityId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "savings_opportunity_id",
                table: "negotiation_outcome",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "savings_opportunity_id",
                table: "negotiation_outcome");
        }
    }
}
