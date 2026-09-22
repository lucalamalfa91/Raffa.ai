using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Chat.Migrations
{
    /// <summary>
    /// ADR-030 (web research, gate 3): the per-tenant, per-UTC-day counter of research calls,
    /// <c>chat_web_research_usage (tenant_id, day, calls)</c> with a composite primary key so the
    /// budget's conditional upsert is one atomic statement. Same forced Row-Level Security policy
    /// as <c>AddTenantRowLevelSecurity</c> gave the two conversation tables (ADR-009).
    /// </summary>
    public partial class AddWebResearchUsage : Migration
    {
        private const string Table = "chat_web_research_usage";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: Table,
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    calls = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_web_research_usage", x => new { x.tenant_id, x.day });
                });

            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{Table}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{Table}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "{Table}"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: Table);
        }
    }
}
