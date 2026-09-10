using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Savings.Migrations
{
    /// <inheritdoc />
    public partial class AddRealizedSavings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "realized_savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    savings_opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    realized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realized_savings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_realized_savings_tenant_id",
                table: "realized_savings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_realized_savings_tenant_id_savings_opportunity_id",
                table: "realized_savings",
                columns: new[] { "tenant_id", "savings_opportunity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "realized_savings");
        }
    }
}
