using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Adds the estimate columns to <c>contract_line_item_market_price</c> (a converted or AI band
    /// for a line with no market match, kept apart from the matched band) and <c>corpus_version</c>
    /// (the market corpus a row was compared against, so a re-ingestion re-prices it). All nullable;
    /// a row written before this migration has no corpus version and is re-priced on its next read.
    /// The table's RLS policy is unchanged.
    /// </summary>
    public partial class AddContractLineItemMarketPriceEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "corpus_version",
                table: "contract_line_item_market_price",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estimate_basis",
                table: "contract_line_item_market_price",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estimate_currency",
                table: "contract_line_item_market_price",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estimate_kind",
                table: "contract_line_item_market_price",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estimate_product",
                table: "contract_line_item_market_price",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimate_unit_price_p25",
                table: "contract_line_item_market_price",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimate_unit_price_p50",
                table: "contract_line_item_market_price",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimate_unit_price_p75",
                table: "contract_line_item_market_price",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estimated_at",
                table: "contract_line_item_market_price",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "corpus_version",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimate_basis",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimate_currency",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimate_kind",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimate_product",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimate_unit_price_p25",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimate_unit_price_p50",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimate_unit_price_p75",
                table: "contract_line_item_market_price");

            migrationBuilder.DropColumn(
                name: "estimated_at",
                table: "contract_line_item_market_price");
        }
    }
}
