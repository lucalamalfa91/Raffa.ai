using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Task E19/F03/US01/T01 (us-01-step-ticks-api). Creates <c>contract_negotiation_step</c>
    /// (ADR-028 §D3; ADR-003 w16 clause 1) <b>and</b> its Row-Level Security policy in this same
    /// migration — ADR-009's w16 footer clause 2 is explicit that a new tenant table's `ENABLE` +
    /// `FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy must not wait for a follow-up
    /// migration the way <c>AddTenantRowLevelSecurity</c> once retrofitted the R0 tables. The
    /// three RLS statements below are copied verbatim from that migration's own per-table
    /// template (also `documents-contracts.sql:421-425`'s shape for `"contract"`) — same
    /// `nullif(current_setting(...), '')::uuid` guard, same `USING`/`WITH CHECK` pair.
    /// <c>ContractNegotiationStep</c> subclasses <c>TenantScopedEntity</c> in
    /// <c>DocumentsContractsDbContext</c> (ADR-009 w16 footer clause 2a), so
    /// <c>TenantRlsMigrationCheckTests</c> discovers this table automatically — no hand-written
    /// per-table RLS test is added or owed.
    /// </summary>
    public partial class AddContractNegotiationStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_negotiation_step",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ticked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_negotiation_step", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_negotiation_step_tenant_id",
                table: "contract_negotiation_step",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_negotiation_step_tenant_id_contract_id_step",
                table: "contract_negotiation_step",
                columns: new[] { "tenant_id", "contract_id", "step" },
                unique: true);

            // ADR-009 w16 footer clause 2 / 2a: RLS ships in the table's own migration, not a
            // follow-up. `FORCE` is what binds the table owner (the role this migration itself
            // runs as), not just an ordinary non-owner role.
            migrationBuilder.Sql(
                """
                ALTER TABLE "contract_negotiation_step" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "contract_negotiation_step" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "contract_negotiation_step"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "contract_negotiation_step";
                ALTER TABLE "contract_negotiation_step" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "contract_negotiation_step" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "contract_negotiation_step");
        }
    }
}
