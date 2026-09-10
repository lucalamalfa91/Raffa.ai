using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Quotes.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteLineNormalizedUnitEconomics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "normalized_annual_unit_price",
                table: "quote_line",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "normalized_term_months",
                table: "quote_line",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "normalized_annual_unit_price",
                table: "quote_line");

            migrationBuilder.DropColumn(
                name: "normalized_term_months",
                table: "quote_line");
        }
    }
}
