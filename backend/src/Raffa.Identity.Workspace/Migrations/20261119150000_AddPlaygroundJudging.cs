using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Raffa.Identity.Workspace.Infrastructure;

#nullable disable

namespace Raffa.Identity.Workspace.Migrations
{
    /// <summary>
    /// Super Admin agent playground, PR 4: custom judging rubrics and the judgements (an LLM judge's scores over
    /// the answers of a comparison). A judgement keeps a snapshot of the rubric it used, so editing a rubric never
    /// changes a past judgement. Platform-level, no tenant content, no RLS; raw SQL like the other playground tables.
    /// </summary>
    [DbContext(typeof(IdentityWorkspaceDbContext))]
    [Migration("20261119150000_AddPlaygroundJudging")]
    public partial class AddPlaygroundJudging : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE playground_rubric (
                    id         uuid PRIMARY KEY,
                    workflow   character varying(40) NOT NULL,
                    name       character varying(120) NOT NULL,
                    criteria   jsonb NOT NULL,
                    created_by character varying(200) NOT NULL,
                    created_at timestamp with time zone NOT NULL,
                    updated_at timestamp with time zone NOT NULL,
                    archived   boolean NOT NULL DEFAULT false
                );

                CREATE TABLE playground_judgement (
                    id                 uuid PRIMARY KEY,
                    comparison_id      uuid NULL,
                    run_ids            jsonb NOT NULL,
                    workflow           character varying(40) NOT NULL,
                    rubric             jsonb NOT NULL,
                    judge_model        character varying(120) NULL,
                    passes             integer NOT NULL,
                    created_by         character varying(200) NOT NULL,
                    created_at         timestamp with time zone NOT NULL,
                    prompt_tokens      integer NOT NULL,
                    completion_tokens  integer NOT NULL,
                    estimated_cost_usd numeric(12,6) NULL,
                    result             jsonb NOT NULL,
                    trace              jsonb NOT NULL
                );

                CREATE INDEX ix_playground_judgement_comparison ON playground_judgement (comparison_id) WHERE comparison_id IS NOT NULL;
                CREATE INDEX ix_playground_judgement_created_at ON playground_judgement (created_at DESC);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS playground_judgement;
                DROP TABLE IF EXISTS playground_rubric;
                """);
        }
    }
}
