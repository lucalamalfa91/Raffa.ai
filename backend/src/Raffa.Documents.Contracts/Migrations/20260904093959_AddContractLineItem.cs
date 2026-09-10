using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Adds the <c>contract_line_item</c> table (product spec §6 "ContractLineItem"; task
    /// E02/F02/US01/T01) and, in the same migration, enables/forces Postgres Row-Level Security
    /// and the same <c>tenant_isolation</c> policy shape <c>AddTenantRowLevelSecurity</c> gave
    /// every table that existed as of that migration (ADR-009). This table did not exist then, so
    /// it needs its own RLS statements now rather than inheriting them — exactly the "future
    /// tenant-scoped table gets its own follow-up migration adding RLS for it" case that
    /// migration's doc comment anticipates. Bundled into this same migration (table + RLS
    /// together) rather than a separate follow-up so there is no migration history state where
    /// <c>contract_line_item</c> exists without RLS. The dynamic CI check
    /// (<c>Raffa.Tenancy.Tests.TenantRlsMigrationCheckTests</c>, which discovers tenant-scoped
    /// tables from every <see cref="Raffa.Documents.Contracts.Domain.TenantScopedEntity"/>
    /// subclass rather than a hardcoded list) verifies this table specifically because
    /// <see cref="Raffa.Documents.Contracts.Domain.ContractLineItem"/> is one.
    /// </summary>
    public partial class AddContractLineItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_line_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sku = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    list_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    discount = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    billing_period = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    annual_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_line_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_line_item_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_contract_id",
                table: "contract_line_item",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_product_id",
                table: "contract_line_item",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_tenant_id",
                table: "contract_line_item",
                column: "tenant_id");

            // ADR-009: this table is tenant-scoped (TenantScopedEntity) and must never ship
            // without RLS — see the type doc comment for why this lives in the same migration
            // that creates the table instead of a separate follow-up.
            migrationBuilder.Sql(
                """
                ALTER TABLE "contract_line_item" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "contract_line_item" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "contract_line_item"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "contract_line_item";
                ALTER TABLE "contract_line_item" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "contract_line_item" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "contract_line_item");
        }
    }
}
