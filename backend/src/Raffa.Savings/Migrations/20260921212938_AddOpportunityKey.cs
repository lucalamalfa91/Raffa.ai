using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Savings.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Ask Raffa as a savings consultant: <c>opportunity_key</c> gives a lever-generated
    /// opportunity a stable identity within its contract so
    /// <c>SavingsOpportunityService.UpsertGeneratedAsync</c> can refresh it turn after turn
    /// instead of inserting duplicates. Partial unique index (tenant, contract, key) where the key
    /// is set; hand-recorded rows keep a null key and stay unconstrained.
    /// </summary>
    public partial class AddOpportunityKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "opportunity_key",
                table: "savings_opportunity",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_savings_opportunity_tenant_id_contract_id_opportunity_key",
                table: "savings_opportunity",
                columns: new[] { "tenant_id", "contract_id", "opportunity_key" },
                unique: true,
                filter: "opportunity_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_savings_opportunity_tenant_id_contract_id_opportunity_key",
                table: "savings_opportunity");

            migrationBuilder.DropColumn(
                name: "opportunity_key",
                table: "savings_opportunity");
        }
    }
}
