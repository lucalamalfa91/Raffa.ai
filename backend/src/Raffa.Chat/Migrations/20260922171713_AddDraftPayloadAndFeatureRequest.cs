using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Chat.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// ADR-030 (Ask Raffa capability gaps + feedback loop): <c>conversation_message.payload_json</c>
    /// carries a turn's structured half (the drafted email, the capability gap, the feedback offer
    /// or result — <c>Application.Reply.ReplyPayload</c>), and <c>feature_request</c> stores the
    /// reports users file from the in-chat feedback card. The new table gets the same Row-Level
    /// Security policy as the two existing tables (ADR-009; see
    /// <c>AddTenantRowLevelSecurity</c> for the SQL's rationale) in this same migration, so no
    /// tenant-scoped table ever exists without it.
    /// </summary>
    public partial class AddDraftPayloadAndFeatureRequest : Migration
    {
        private const string RlsTable = "feature_request";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payload_json",
                table: "conversation_message",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "feature_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gap_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    gap_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    workspace_hash = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issue_number = table.Column<int>(type: "integer", nullable: true),
                    issue_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    publish_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feature_request_tenant_id",
                table: "feature_request",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_request_tenant_id_message_id",
                table: "feature_request",
                columns: new[] { "tenant_id", "message_id" },
                unique: true);

            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{RlsTable}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{RlsTable}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "{RlsTable}"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS tenant_isolation ON "{RlsTable}";
                ALTER TABLE "{RlsTable}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "{RlsTable}" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "feature_request");

            migrationBuilder.DropColumn(
                name: "payload_json",
                table: "conversation_message");
        }
    }
}
