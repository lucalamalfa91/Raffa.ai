using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Savings.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiableSavings : Migration
    {
        private static readonly string[] TenantScopedTables =
        [
            "saving_result",
            "invoice",
            "saving_realization",
            "savings_goal",
            "case_objective",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_objective",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pct = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_objective", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saving_result",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contract_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    counts_as_negotiation = table.Column<bool>(type: "boolean", nullable: false),
                    amount_annual = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    baseline_desc = table.Column<string>(type: "text", nullable: false),
                    baseline_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    new_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    method = table.Column<string>(type: "text", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: true),
                    period_to = table.Column<DateOnly>(type: "date", nullable: true),
                    period_desc = table.Column<string>(type: "text", nullable: false),
                    evidence_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saving_result", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "savings_goal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    pct = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    annual_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    period = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_savings_goal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saving_realization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    saving_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    realized_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saving_realization", x => x.id);
                    table.ForeignKey(
                        name: "fk_saving_realization_invoice_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saving_realization_saving_result_saving_result_id",
                        column: x => x.saving_result_id,
                        principalTable: "saving_result",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_objective_tenant_id",
                table: "case_objective",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_objective_tenant_id_case_id",
                table: "case_objective",
                columns: new[] { "tenant_id", "case_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_objective_tenant_id_contract_id",
                table: "case_objective",
                columns: new[] { "tenant_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_tenant_id",
                table: "invoice",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_tenant_id_contract_id_period",
                table: "invoice",
                columns: new[] { "tenant_id", "contract_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_saving_realization_invoice_id",
                table: "saving_realization",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_saving_realization_saving_result_id_invoice_id",
                table: "saving_realization",
                columns: new[] { "saving_result_id", "invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saving_realization_tenant_id",
                table: "saving_realization",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_saving_result_tenant_id",
                table: "saving_result",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_saving_result_tenant_id_contract_id",
                table: "saving_result",
                columns: new[] { "tenant_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saving_result_tenant_id_signed_on",
                table: "saving_result",
                columns: new[] { "tenant_id", "signed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_savings_goal_tenant_id",
                table: "savings_goal",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_savings_goal_active_per_user",
                table: "savings_goal",
                columns: new[] { "tenant_id", "user_id" },
                unique: true,
                filter: "active");

            // Tenant isolation (BACKEND-SPEC §2 "all with tenant_id + RLS policy tenant_isolation"),
            // the same policy as the module's other tables.
            foreach (var table in TenantScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON "{table}"
                        USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantScopedTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "case_objective");

            migrationBuilder.DropTable(
                name: "saving_realization");

            migrationBuilder.DropTable(
                name: "savings_goal");

            migrationBuilder.DropTable(
                name: "invoice");

            migrationBuilder.DropTable(
                name: "saving_result");
        }
    }
}
