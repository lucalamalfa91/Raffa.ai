using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Raffa.Audit.Migrations
{
    /// <summary>
    /// Makes "append-only" (this task's own coding objective; Appendix C rule 9 — capture
    /// corrections/outcomes from day one, and rule 5's same never-destructively-overwrite spirit
    /// applied to the audit trail itself) a database-enforced guarantee rather than only an
    /// API-shape convention. <see cref="Infrastructure.AuditWriter"/> never exposes an
    /// update/delete method, but that alone only stops a well-behaved caller going through this
    /// module's own port — a future bug (a raw SQL statement, a mistaken `DbSet.Update`/`Remove`
    /// call, a different tool connecting straight to Postgres) could still mutate history. A
    /// `BEFORE UPDATE OR DELETE` trigger closes that gap unconditionally: unlike Row-Level
    /// Security's `FORCE` clause (which only changes whether a *policy* applies to the table
    /// owner), a trigger fires for every session that attempts the statement — table owner,
    /// superuser, or ordinary role alike — so this holds regardless of which Postgres role the
    /// application, a migration, or an operator happens to be connected as.
    ///
    /// `RAISE EXCEPTION ... USING ERRCODE = 'insufficient_privilege'` gives callers a stable,
    /// documented SQLSTATE (`42501`, surfaced by Npgsql as
    /// <c>Npgsql.PostgresException.SqlState</c> /
    /// <c>Npgsql.PostgresErrorCodes.InsufficientPrivilege</c>) to branch on, rather than only a
    /// free-text message. The function deliberately does not reference `OLD`/`NEW` column values
    /// (only `TG_OP`) so it never has to special-case that `NEW` is unassigned on a `DELETE`
    /// trigger.
    /// </summary>
    public partial class AddAppendOnlyEnforcement : Migration
    {
        private const string FunctionName = "audit_event_reject_mutation";
        private const string TriggerName = "audit_event_append_only";
        private const string TableName = "audit_event";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                CREATE FUNCTION {FunctionName}() RETURNS trigger
                    LANGUAGE plpgsql
                    AS $BODY$
                    BEGIN
                        RAISE EXCEPTION 'audit_event is append-only: % is not permitted', TG_OP
                            USING ERRCODE = 'insufficient_privilege';
                    END;
                    $BODY$;

                CREATE TRIGGER {TriggerName}
                    BEFORE UPDATE OR DELETE ON "{TableName}"
                    FOR EACH ROW
                    EXECUTE FUNCTION {FunctionName}();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                DROP TRIGGER IF EXISTS {TriggerName} ON "{TableName}";
                DROP FUNCTION IF EXISTS {FunctionName}();
                """);
        }
    }
}
