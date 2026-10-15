using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Market.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketRecordPayloadHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payload_hash",
                table: "market_record",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payload_hash",
                table: "market_record");
        }
    }
}
