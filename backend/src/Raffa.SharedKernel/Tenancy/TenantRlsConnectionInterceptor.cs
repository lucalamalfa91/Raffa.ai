using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Raffa.SharedKernel.Tenancy;

/// <summary>
/// EF Core connection interceptor that is the data-access-layer half of ADR-009's tenant
/// isolation, extended by the ADR-009 w14 footer (clauses 1-5) and ADR-025 §F.2 to also carry the
/// caller's identity. It sets two independent Postgres session GUCs once when a connection is
/// opened for the current request/worker job -- `app.tenant_id` from
/// <see cref="ITenantContext.Current"/> and `app.identity_subject` from
/// <see cref="ICallerIdentityContext.Current"/> -- and clears both when the connection is
/// returned to the pool, so a pooled connection can never leak either claim to whoever borrows it
/// next. Postgres Row-Level Security policies (`current_setting('app.tenant_id', true)`,
/// `current_setting('app.identity_subject', true)`) are the non-bypassable backstop these claims
/// feed; this interceptor never runs as `BYPASSRLS` and never widens access, it only ever narrows
/// it.
///
/// The two claims are deliberately independent (ADR-009 w14 footer clause 1): either may be set
/// without the other. <c>GET /api/workspaces</c> (NW-01) is exactly the case that needs
/// `app.identity_subject` set with **no** `app.tenant_id` at all, to discover which tenants the
/// caller belongs to before any tenant scope exists. When a claim's source
/// (<see cref="ITenantContext.Current"/> / <see cref="ICallerIdentityContext.Current"/>) is
/// <see langword="null"/> (or, for the identity, empty after normalisation), that claim is left
/// unset -- never an empty-string stand-in -- so RLS denies every row the corresponding policy
/// would otherwise widen. Fail closed, never fail open.
///
/// The `callerIdentityContext` constructor parameter is optional (defaults to
/// <see langword="null"/>) so every existing call site across every module's
/// `*DbContextOptions.Configure` (constructed today with only a tenant context) keeps compiling
/// and behaving exactly as before; only a caller that actually needs the identity claim supplies
/// one. Supplying <see langword="null"/> is equivalent to an identity scope that is never entered
/// -- the identity claim is simply never set on that DbContext's connections.
///
/// Round trips: EF Core opens and closes the connection around every command, so this class runs
/// on every query. It therefore costs one extra command on open (all active claims in a single
/// `SELECT set_config(...)`) and one on close (both `RESET`s in a single command), not one command
/// per claim.
/// </summary>
public sealed class TenantRlsConnectionInterceptor(
    ITenantContext tenantContext,
    ICallerIdentityContext? callerIdentityContext = null) : DbConnectionInterceptor
{
    internal const string TenantSettingName = "app.tenant_id";
    internal const string IdentitySettingName = "app.identity_subject";

    private const string TenantParameterName = "tenant_id";
    private const string IdentityParameterName = "identity";

    // Both GUCs, always, in one command: see the "Close" note below.
    private const string ResetClaimsCommandText = $"RESET {TenantSettingName}; RESET {IdentitySettingName}";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetClaims(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetClaimsAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ClearClaims(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        await ClearClaimsAsync(connection).ConfigureAwait(false);
        return await base.ConnectionClosingAsync(connection, eventData, result).ConfigureAwait(false);
    }

    // --- Open: one round trip for however many claims are active ---------------------------------
    //
    // Both claims travel in a single `SELECT set_config(...)` (one statement, one network round
    // trip) instead of one command per claim. A claim whose source is null/empty is simply left out
    // of the statement, so it stays genuinely unset -- never an empty-string stand-in -- and when
    // neither claim is active no command is issued at all.
    //
    // The tenant claim used to be written with `SET app.tenant_id = '<guid>'` (inlined, because
    // `SET` cannot take a bind parameter). `set_config(name, value, false)` is the same operation:
    // Postgres runs both through `set_config_option(..., PGC_S_SESSION, GUC_ACTION_SET)`, so the
    // value is session-scoped, is undone by an aborted transaction exactly as `SET` is, and reads
    // back identically through `current_setting`. It additionally accepts a real bind parameter,
    // so neither claim is ever interpolated into the command text. The tenant value is the Guid's
    // "D" format, exactly the text the old `SET` inlined.

    private void SetClaims(DbConnection connection)
    {
        var claims = ReadClaims();
        if (claims.IsEmpty)
        {
            return;
        }

        using var command = connection.CreateCommand();
        PrepareSetClaimsCommand(command, claims);
        command.ExecuteNonQuery();
    }

    private async Task SetClaimsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var claims = ReadClaims();
        if (claims.IsEmpty)
        {
            return;
        }

        var command = connection.CreateCommand();
        await using var _ = command.ConfigureAwait(false);
        PrepareSetClaimsCommand(command, claims);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Both ambient sources are read once, so the single statement is built from one snapshot.
    private Claims ReadClaims()
    {
        var identity = callerIdentityContext?.Current;
        return new Claims(
            tenantContext.Current,
            // An empty value that survived normalisation counts as "no identity scope": leave the
            // GUC genuinely unset rather than set it to ''. The `identity_self` policy's own
            // `nullif(current_setting(...), '') IS NOT NULL` guard would deny either way, but
            // "unset" is the honest signal for "no caller identity", not a stand-in.
            string.IsNullOrEmpty(identity) ? null : identity);
    }

    // --- Binding rule (applies to both claims) ---------------------------------------------------
    //
    // Claim values are written through bind parameters, never interpolated. A `TenantId` wraps a
    // `Guid`, whose "D" format is constrained to hex digits and hyphens, but an identity is
    // caller-controlled text (`X-User-Id`, ADR-025 §A) with no such guarantee: inlining it into a
    // statement would let an identity such as `x'; SET app.tenant_id = '<victim>'; --` repoint the
    // tenant claim on this same connection for the rest of the request -- exactly the cross-tenant
    // path ADR-009 (`:12-13`) forbids. `SET`/custom GUCs cannot accept a bind parameter at all, so
    // the remedy is a statement form that can: `SELECT set_config(name, value, is_local)` is a
    // regular statement and accepts `value` as a real bind parameter. Do not "tidy" a write below
    // back into `SET` or into string interpolation of a value: that reopens the injection sink.
    //
    // The third argument is always `false` (session-scoped), never `true` (transaction-local): the
    // statement issued on open runs in its own implicit single-statement transaction, so a
    // transaction-local value would already have reverted before the caller's own first query runs
    // against this connection -- silently vanishing before the discovery read that needs it
    // (ADR-009 w14 footer clause 3, ADR-025 §F.2b).

    /// <summary>
    /// Builds <c>SELECT set_config('app.tenant_id', @tenant_id, false), set_config('app.identity_subject', @identity, false)</c>,
    /// containing only the entries whose claim is present, with every value bound through a real
    /// <see cref="DbParameter"/> -- a genuine `NpgsqlParameter` at runtime, since this interceptor
    /// only ever runs against the Npgsql provider, obtained via the provider-agnostic
    /// <see cref="DbCommand.CreateParameter"/> so this shared-kernel file keeps no direct Npgsql
    /// package reference (see Raffa.SharedKernel.csproj's own comment). The setting names are our
    /// own constants, never caller input, so interpolating *them* into the command text is not the
    /// injection the type doc comment guards against; the claim values are, and they go in
    /// exclusively through the bind parameters, never into <see cref="DbCommand.CommandText"/>.
    /// </summary>
    private static void PrepareSetClaimsCommand(DbCommand command, Claims claims)
    {
        var calls = new List<string>(2);

        if (claims.Tenant is { } tenantId)
        {
            calls.Add($"set_config('{TenantSettingName}', @{TenantParameterName}, false)");
            AddParameter(command, TenantParameterName, tenantId.Value.ToString("D"));
        }

        if (claims.Identity is { } identity)
        {
            calls.Add($"set_config('{IdentitySettingName}', @{IdentityParameterName}, false)");
            AddParameter(command, IdentityParameterName, identity);
        }

        command.CommandText = "SELECT " + string.Join(", ", calls);
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    // --- Close: one round trip, both claims, unconditionally -------------------------------------
    //
    // Both GUCs are reset in a single command (two statements, one network round trip) whenever the
    // connection is closed while still open. They are deliberately NOT narrowed to "the claims
    // this interceptor set on open": that would save nothing (the second RESET rides in the same
    // round trip) and would add leak risk -- the ambient contexts can differ between open and
    // close, and other code may have set a claim on the same connection directly. A pooled
    // connection must never carry either claim to its next borrower. A connection that is no
    // longer open (already broken or closed) is left alone, as before.

    private static void ClearClaims(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = ResetClaimsCommandText;
        command.ExecuteNonQuery();
    }

    private static async Task ClearClaimsAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        var command = connection.CreateCommand();
        await using var _ = command.ConfigureAwait(false);
        command.CommandText = ResetClaimsCommandText;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private readonly record struct Claims(TenantId? Tenant, string? Identity)
    {
        public bool IsEmpty => Tenant is null && Identity is null;
    }
}
