using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Contigo.Market.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "market_record",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    feed_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    provenance_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_record", x => x.record_id);
                });

            migrationBuilder.CreateTable(
                name: "market_embedding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    chunk_text = table.Column<string>(type: "text", nullable: false),
                    vector = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_embedding", x => x.id);
                    table.ForeignKey(
                        name: "fk_market_embedding_market_record_record_id",
                        column: x => x.record_id,
                        principalTable: "market_record",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_market_embedding_record_id",
                table: "market_embedding",
                column: "record_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_record_provider",
                table: "market_record",
                column: "provider");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_embedding");

            migrationBuilder.DropTable(
                name: "market_record");
        }
    }
}
