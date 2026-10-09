using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Market.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingFixtureFlagAndHnswIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_fixture",
                table: "market_embedding",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_market_embedding_vector",
                table: "market_embedding",
                column: "vector")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })
                .Annotation("Npgsql:StorageParameter:ef_construction", 64)
                .Annotation("Npgsql:StorageParameter:m", 16);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_market_embedding_vector",
                table: "market_embedding");

            migrationBuilder.DropColumn(
                name: "is_fixture",
                table: "market_embedding");
        }
    }
}
