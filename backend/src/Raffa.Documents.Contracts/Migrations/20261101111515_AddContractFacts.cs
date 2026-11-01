using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <summary>AskRaffa 4.0: <c>contract_facts</c> (jsonb profile per contract) under RLS.</summary>
    public partial class AddContractFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facts = table.Column<string>(type: "jsonb", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_facts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_facts_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_facts_contract_id",
                table: "contract_facts",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_facts_tenant_id",
                table: "contract_facts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_facts_tenant_id_contract_id",
                table: "contract_facts",
                columns: new[] { "tenant_id", "contract_id" },
                unique: true);

            // Same tenant backstop as every sibling table (ADR-009).
            migrationBuilder.Sql(
                """
                ALTER TABLE "contract_facts" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "contract_facts" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "contract_facts"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "contract_facts";
                ALTER TABLE "contract_facts" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "contract_facts" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "contract_facts");
        }
    }
}
