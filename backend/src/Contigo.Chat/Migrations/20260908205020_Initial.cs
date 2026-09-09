using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contigo.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    scope_contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    markdown = table.Column<string>(type: "text", nullable: false),
                    citations_json = table.Column<string>(type: "jsonb", nullable: false),
                    actions_json = table.Column<string>(type: "jsonb", nullable: false),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    input_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_message_conversation_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tenant_id",
                table: "conversation",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tenant_id_user_id_updated_at",
                table: "conversation",
                columns: new[] { "tenant_id", "user_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_conversation_id_created_at",
                table: "conversation_message",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_tenant_id",
                table: "conversation_message",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_message");

            migrationBuilder.DropTable(
                name: "conversation");
        }
    }
}
