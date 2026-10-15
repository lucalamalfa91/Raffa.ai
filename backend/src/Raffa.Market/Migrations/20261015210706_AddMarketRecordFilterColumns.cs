using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Market.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketRecordFilterColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "market_record",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "market_record",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geography",
                table: "market_record",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_norm",
                table: "market_record",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_norm",
                table: "market_record",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "term_months",
                table: "market_record",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit_metric",
                table: "market_record",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_record_category_geography",
                table: "market_record",
                columns: new[] { "category", "geography" });

            migrationBuilder.CreateIndex(
                name: "ix_market_record_product_norm",
                table: "market_record",
                column: "product_norm");

            migrationBuilder.CreateIndex(
                name: "ix_market_record_supplier_norm",
                table: "market_record",
                column: "supplier_norm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_market_record_category_geography",
                table: "market_record");

            migrationBuilder.DropIndex(
                name: "ix_market_record_product_norm",
                table: "market_record");

            migrationBuilder.DropIndex(
                name: "ix_market_record_supplier_norm",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "category",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "geography",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "product_norm",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "supplier_norm",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "term_months",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "unit_metric",
                table: "market_record");
        }
    }
}
