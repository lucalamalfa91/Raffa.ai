using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Documents.Contracts.Migrations
{
    /// <inheritdoc />
    /// <summary>AskRaffa 4.0 slice 4: <c>contract_point_decision</c>, <c>contract_term_version</c>,
    /// <c>document_field_decision</c>, all under RLS.</summary>
    public partial class AddContractsV4DecisionsTermsReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_point_decision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_point_decision", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_point_decision_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_term_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    notice_days = table.Column<int>(type: "integer", nullable: true),
                    annual_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    auto_renew = table.Column<bool>(type: "boolean", nullable: true),
                    uplift_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_term_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_term_version_contract_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_field_decision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_field_decision", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_field_decision_document_document_id",
                        column: x => x.document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_point_decision_contract_id",
                table: "contract_point_decision",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_point_decision_tenant_id",
                table: "contract_point_decision",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_point_decision_tenant_id_contract_id_point_key",
                table: "contract_point_decision",
                columns: new[] { "tenant_id", "contract_id", "point_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_term_version_contract_id",
                table: "contract_term_version",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_term_version_tenant_id",
                table: "contract_term_version",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_term_version_tenant_id_contract_id",
                table: "contract_term_version",
                columns: new[] { "tenant_id", "contract_id" },
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_document_field_decision_document_id",
                table: "document_field_decision",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_field_decision_tenant_id",
                table: "document_field_decision",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_field_decision_tenant_id_document_id_field_key",
                table: "document_field_decision",
                columns: new[] { "tenant_id", "document_id", "field_key" },
                unique: true);

            // Same tenant backstop as every sibling table (ADR-009).
            migrationBuilder.Sql(
                """
                ALTER TABLE "contract_point_decision" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "contract_point_decision" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "contract_point_decision"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
            migrationBuilder.Sql(
                """
                ALTER TABLE "contract_term_version" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "contract_term_version" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "contract_term_version"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
            migrationBuilder.Sql(
                """
                ALTER TABLE "document_field_decision" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "document_field_decision" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "document_field_decision"
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "contract_point_decision";
                ALTER TABLE "contract_point_decision" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "contract_point_decision" DISABLE ROW LEVEL SECURITY;
                """);
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "contract_term_version";
                ALTER TABLE "contract_term_version" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "contract_term_version" DISABLE ROW LEVEL SECURITY;
                """);
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON "document_field_decision";
                ALTER TABLE "document_field_decision" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "document_field_decision" DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "contract_point_decision");

            migrationBuilder.DropTable(
                name: "contract_term_version");

            migrationBuilder.DropTable(
                name: "document_field_decision");
        }
    }
}
