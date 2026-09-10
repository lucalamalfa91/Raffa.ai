using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <summary>
    /// Task E05/F01/US02/T01 (sku-normalization): adds <c>QuoteLine.NormalizedSku</c>/
    /// <c>NormalizedEdition</c>/<c>MatchStatus</c> to the existing <c>quote_line</c> table and
    /// creates this module's second tenant-scoped table, <c>sku_product_mapping</c> (see
    /// <see cref="Raffa.Quotes.Domain.SkuProductMapping"/>'s own doc comment). Also enables
    /// Postgres Row-Level Security on that new table in the same migration — same `ENABLE` /
    /// `FORCE` / `CREATE POLICY` SQL and `nullif(current_setting(...), '')::uuid` NULL-safety guard
    /// as the existing <see cref="AddTenantRowLevelSecurity"/> migration (see that migration's own
    /// doc comment for the full reasoning); combined here into one migration, rather than a
    /// separate follow-up one, since this table's RLS policy is not a retrofit onto pre-existing
    /// data — the table itself does not exist before this migration runs.
    /// <see cref="Raffa.Quotes.Tests.QuoteRlsMigrationCheckTests"/> discovers this table
    /// dynamically from the EF model and would fail the build without this.
    /// </summary>
    public partial class AddSkuProductMapping : Migration
    {
        private const string SkuProductMappingTable = "sku_product_mapping";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "match_status",
                table: "quote_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unmatched");

            migrationBuilder.AddColumn<string>(
                name: "normalized_edition",
                table: "quote_line",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_sku",
                table: "quote_line",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sku_product_mapping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_edition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    canonical_sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    canonical_edition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    canonical_product_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sku_product_mapping", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sku_product_mapping_tenant_id",
                table: "sku_product_mapping",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sku_product_mapping_tenant_id_normalized_sku",
                table: "sku_product_mapping",
                columns: new[] { "tenant_id", "normalized_sku" },
                unique: true);

            // ADR-009: same RLS SQL as Migrations.AddTenantRowLevelSecurity, applied to this one
            // new table (see this migration's own class doc comment for why it is combined here
            // rather than a separate follow-up migration).
            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{SkuProductMappingTable}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{SkuProductMappingTable}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "{SkuProductMappingTable}"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS tenant_isolation ON "{SkuProductMappingTable}";
                ALTER TABLE "{SkuProductMappingTable}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "{SkuProductMappingTable}" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "sku_product_mapping");

            migrationBuilder.DropColumn(
                name: "match_status",
                table: "quote_line");

            migrationBuilder.DropColumn(
                name: "normalized_edition",
                table: "quote_line");

            migrationBuilder.DropColumn(
                name: "normalized_sku",
                table: "quote_line");
        }
    }
}
