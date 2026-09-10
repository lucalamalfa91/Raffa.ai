using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Suppliers.Products.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    country = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_tenant_id",
                table: "supplier",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_tenant_id_normalized_name",
                table: "supplier",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier");
        }
    }
}
