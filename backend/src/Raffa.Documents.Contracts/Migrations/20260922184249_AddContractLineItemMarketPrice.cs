using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Creates <c>contract_line_item_market_price</c> (each line item's last market comparison,
    /// <c>LineItemMarketPriceService</c>) <b>and</b> its Row-Level Security policy in this same
    /// migration (ADR-009 w16 footer clause 2) — the same three statements
    /// <c>AddContractNegotiationStep</c> ships for its own table.
    /// </summary>
    public partial class AddContractLineItemMarketPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_line_item_market_price",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    product = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    geography = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    term_months = table.Column<int>(type: "integer", nullable: true),
                    unit_price_p25 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_price_p50 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    unit_price_p75 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    sample_size = table.Column<int>(type: "integer", nullable: true),
                    provenance = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    market_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_line_item_market_price", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_line_item_market_price_contract_line_item_line_ite",
                        column: x => x.line_item_id,
                        principalTable: "contract_line_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_market_price_contract_id",
                table: "contract_line_item_market_price",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_market_price_line_item_id",
                table: "contract_line_item_market_price",
                column: "line_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_market_price_tenant_id",
                table: "contract_line_item_market_price",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_line_item_market_price_tenant_id_line_item_id",
                table: "contract_line_item_market_price",
                columns: new[] { "tenant_id", "line_item_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE "contract_line_item_market_price" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "contract_line_item_market_price" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "contract_line_item_market_price"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "contract_line_item_market_price";
                ALTER TABLE "contract_line_item_market_price" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "contract_line_item_market_price" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "contract_line_item_market_price");
        }
    }
}
