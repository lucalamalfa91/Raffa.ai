using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Market.Migrations
{
    /// <inheritdoc />
    public partial class AddCorpusVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_market_embedding_market_record_record_id",
                table: "market_embedding");

            migrationBuilder.DropPrimaryKey(
                name: "pk_market_record",
                table: "market_record");

            migrationBuilder.DropIndex(
                name: "ix_market_embedding_record_id",
                table: "market_embedding");

            migrationBuilder.AddColumn<string>(
                name: "corpus_version",
                table: "market_record",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "initial");

            migrationBuilder.AddColumn<string>(
                name: "corpus_version",
                table: "market_embedding",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "initial");

            migrationBuilder.AddPrimaryKey(
                name: "pk_market_record",
                table: "market_record",
                columns: new[] { "record_id", "corpus_version" });

            migrationBuilder.CreateTable(
                name: "market_corpus_version",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    feed_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_corpus_version", x => x.version);
                });

            migrationBuilder.InsertData(
                table: "market_corpus_version",
                columns: new[] { "version", "activated_at", "created_at", "feed_version", "state" },
                values: new object[] { "initial", new DateTimeOffset(new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateTimeOffset(new DateTime(2026, 9, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "active" });

            migrationBuilder.CreateIndex(
                name: "ix_market_record_corpus_version",
                table: "market_record",
                column: "corpus_version");

            migrationBuilder.CreateIndex(
                name: "ix_market_embedding_corpus_version",
                table: "market_embedding",
                column: "corpus_version");

            migrationBuilder.CreateIndex(
                name: "ix_market_embedding_record_id_corpus_version",
                table: "market_embedding",
                columns: new[] { "record_id", "corpus_version" });

            migrationBuilder.CreateIndex(
                name: "ux_market_corpus_version_active",
                table: "market_corpus_version",
                column: "state",
                unique: true,
                filter: "state = 'active'");

            migrationBuilder.AddForeignKey(
                name: "fk_market_embedding_market_record_record_id_corpus_version",
                table: "market_embedding",
                columns: new[] { "record_id", "corpus_version" },
                principalTable: "market_record",
                principalColumns: new[] { "record_id", "corpus_version" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_market_embedding_market_record_record_id_corpus_version",
                table: "market_embedding");

            migrationBuilder.DropTable(
                name: "market_corpus_version");

            migrationBuilder.DropPrimaryKey(
                name: "pk_market_record",
                table: "market_record");

            migrationBuilder.DropIndex(
                name: "ix_market_record_corpus_version",
                table: "market_record");

            migrationBuilder.DropIndex(
                name: "ix_market_embedding_corpus_version",
                table: "market_embedding");

            migrationBuilder.DropIndex(
                name: "ix_market_embedding_record_id_corpus_version",
                table: "market_embedding");

            migrationBuilder.DropColumn(
                name: "corpus_version",
                table: "market_record");

            migrationBuilder.DropColumn(
                name: "corpus_version",
                table: "market_embedding");

            migrationBuilder.AddPrimaryKey(
                name: "pk_market_record",
                table: "market_record",
                column: "record_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_embedding_record_id",
                table: "market_embedding",
                column: "record_id");

            migrationBuilder.AddForeignKey(
                name: "fk_market_embedding_market_record_record_id",
                table: "market_embedding",
                column: "record_id",
                principalTable: "market_record",
                principalColumn: "record_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
