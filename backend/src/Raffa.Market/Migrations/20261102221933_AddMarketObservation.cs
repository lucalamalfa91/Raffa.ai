using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Market.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_observation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    observation_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    supplier_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    geography = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    product = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    term_months = table.Column<int>(type: "integer", nullable: true),
                    discount_pct = table.Column<double>(type: "double precision", nullable: true),
                    uplift_cap_pct = table.Column<double>(type: "double precision", nullable: true),
                    notice_days = table.Column<int>(type: "integer", nullable: true),
                    payment_terms = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_observation", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_market_observation_supplier_term",
                table: "market_observation",
                columns: new[] { "supplier_key", "term_months" });

            migrationBuilder.CreateIndex(
                name: "ux_market_observation_key",
                table: "market_observation",
                column: "observation_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_observation");
        }
    }
}
