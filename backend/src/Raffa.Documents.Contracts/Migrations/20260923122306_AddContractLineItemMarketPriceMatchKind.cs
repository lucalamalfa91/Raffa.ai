using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>
    /// Adds <c>match_kind</c> to <c>contract_line_item_market_price</c>: whether a stored market band
    /// is the line's own product, a bundle of the products it names, or only a similar product
    /// (<c>MarketMatchKind</c>). Nullable — a no-match row has none, and a row written before this
    /// column is re-priced on its next read. The table's RLS policy is unchanged.
    /// </summary>
    public partial class AddContractLineItemMarketPriceMatchKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "match_kind",
                table: "contract_line_item_market_price",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "match_kind",
                table: "contract_line_item_market_price");
        }
    }
}
