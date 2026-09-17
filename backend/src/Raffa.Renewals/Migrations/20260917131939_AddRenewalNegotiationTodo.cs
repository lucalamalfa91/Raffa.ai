using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Renewals.Migrations
{
    /// <summary>
    /// Task E29/F01/US01/T01 (todo-entity-api; wave w19 NW-85): creates this module's third
    /// tenant-scoped table, <c>renewal_negotiation_todo</c> (see
    /// <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo"/>'s own doc comment). Also enables
    /// Postgres Row-Level Security on that new table in the same migration — same
    /// <c>ENABLE</c> / <c>FORCE</c> / <c>CREATE POLICY</c> SQL and
    /// <c>nullif(current_setting(...), '')::uuid</c> NULL-safety guard as
    /// <see cref="AddTenantRowLevelSecurity"/>/<see cref="AddRenewalAlert"/> — combined here, rather
    /// than a separate follow-up migration, since this table's RLS policy is not a retrofit onto
    /// pre-existing data (the table itself does not exist before this migration runs; ADR-009
    /// w16 §2/w19). <c>Raffa.Renewals.Tests.RenewalActionRlsMigrationCheckTests</c> (a generic,
    /// EF-model-driven check despite its own name — see that type's own doc comment) discovers this
    /// table dynamically and would fail the build without this.
    /// </summary>
    public partial class AddRenewalNegotiationTodo : Migration
    {
        private const string RenewalNegotiationTodoTable = "renewal_negotiation_todo";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "renewal_negotiation_todo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    current = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    citation_keys = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renewal_negotiation_todo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_renewal_negotiation_todo_tenant_contract_point_key",
                table: "renewal_negotiation_todo",
                columns: new[] { "tenant_id", "contract_id", "point_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_renewal_negotiation_todo_tenant_id",
                table: "renewal_negotiation_todo",
                column: "tenant_id");

            // ADR-009: same RLS SQL as Migrations.AddTenantRowLevelSecurity/AddRenewalAlert, applied
            // to this one new table (see this migration's own class doc comment for why it is
            // combined here rather than a separate follow-up migration).
            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{RenewalNegotiationTodoTable}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{RenewalNegotiationTodoTable}" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "{RenewalNegotiationTodoTable}"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS tenant_isolation ON "{RenewalNegotiationTodoTable}";
                ALTER TABLE "{RenewalNegotiationTodoTable}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "{RenewalNegotiationTodoTable}" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "renewal_negotiation_todo");
        }
    }
}
