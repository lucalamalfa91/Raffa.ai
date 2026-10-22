using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Tenancy.Tests;

/// <summary>
/// Docker-free proof of exactly which commands <see cref="TenantRlsConnectionInterceptor"/> puts on
/// the wire (EF Core opens and closes the connection around every query, so these commands are paid
/// on each one): a fake <see cref="DbConnection"/> records every command's text, parameters and
/// execution path. The Postgres-side meaning of those commands (RLS actually filtering, the GUC
/// reading back) stays with <see cref="TenantRlsIdentitySubjectTests"/> and
/// <see cref="TenantRlsCrossTenantIsolationTests"/>, which need Docker.
///
/// What is pinned here is the security shape that must survive any round-trip optimisation: one
/// command per open with whichever claims are active and nothing when none is (fail closed, never an
/// empty-string stand-in); claim values only ever as bind parameters, never in the command text; and
/// both GUCs reset on every close of a connection that is still open, whatever the ambient context
/// looks like at that moment.
/// </summary>
public sealed class TenantRlsConnectionInterceptorCommandTests
{
    private const string BothClaimsText =
        "SELECT set_config('app.tenant_id', @tenant_id, false), set_config('app.identity_subject', @identity, false)";

    private const string TenantOnlyText = "SELECT set_config('app.tenant_id', @tenant_id, false)";
    private const string IdentityOnlyText = "SELECT set_config('app.identity_subject', @identity, false)";
    private const string ResetText = "RESET app.tenant_id; RESET app.identity_subject";

    public static TheoryData<bool> SyncAndAsync => new() { true, false };

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task Both_claims_are_set_by_one_command_with_bound_values(bool sync)
    {
        var tenantId = TenantId.New();
        var (interceptor, connection, scopes) = Arrange(tenantId, "caller@example.com");
        using var _ = scopes;

        await OpenAsync(interceptor, connection, sync);

        var command = Assert.Single(connection.Commands);
        Assert.Equal(BothClaimsText, command.CommandText);
        Assert.Equal(sync ? ExecutionPath.Sync : ExecutionPath.Async, command.Path);
        Assert.Equal(
            new[] { ("tenant_id", (object?)tenantId.Value.ToString("D")), ("identity", "caller@example.com") },
            command.Parameters);
        Assert.True(command.Disposed);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task Tenant_only_leaves_the_identity_guc_untouched(bool sync)
    {
        var tenantId = TenantId.New();
        var (interceptor, connection, scopes) = Arrange(tenantId, identity: null);
        using var _ = scopes;

        await OpenAsync(interceptor, connection, sync);

        var command = Assert.Single(connection.Commands);
        Assert.Equal(TenantOnlyText, command.CommandText);
        Assert.Equal(new[] { ("tenant_id", (object?)tenantId.Value.ToString("D")) }, command.Parameters);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task Identity_only_leaves_the_tenant_guc_untouched(bool sync)
    {
        // GET /api/workspaces discovery (NW-01): identity set with no tenant scope at all.
        var (interceptor, connection, scopes) = Arrange(tenantId: null, "discovery@example.com");
        using var _ = scopes;

        await OpenAsync(interceptor, connection, sync);

        var command = Assert.Single(connection.Commands);
        Assert.Equal(IdentityOnlyText, command.CommandText);
        Assert.Equal(new[] { ("identity", (object?)"discovery@example.com") }, command.Parameters);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task No_active_claim_issues_no_command_at_all(bool sync)
    {
        var (interceptor, connection, scopes) = Arrange(tenantId: null, identity: null);
        using var _ = scopes;

        await OpenAsync(interceptor, connection, sync);

        Assert.Empty(connection.Commands);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task Empty_identity_is_treated_as_unset_not_written_as_an_empty_string(bool sync)
    {
        var tenantContext = new TenantContext();
        var identityContext = new FixedIdentityContext(string.Empty);
        var interceptor = new TenantRlsConnectionInterceptor(tenantContext, identityContext);
        var connection = new RecordingDbConnection();

        await OpenAsync(interceptor, connection, sync);

        Assert.Empty(connection.Commands);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task Caller_controlled_identity_travels_only_as_a_parameter(bool sync)
    {
        // The shape that would repoint the tenant claim if the identity were ever interpolated.
        const string hostile = "x'; SET app.tenant_id = '11111111-1111-1111-1111-111111111111'; --";
        var tenantId = TenantId.New();
        var (interceptor, connection, scopes) = Arrange(tenantId, hostile);
        using var _ = scopes;

        await OpenAsync(interceptor, connection, sync);

        var command = Assert.Single(connection.Commands);
        Assert.Equal(BothClaimsText, command.CommandText);
        Assert.DoesNotContain("11111111", command.CommandText);
        Assert.DoesNotContain(tenantId.Value.ToString("D"), command.CommandText);
        Assert.Contains(("identity", (object?)hostile), command.Parameters);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task A_scope_that_has_ended_stops_being_claimed_on_the_next_open(bool sync)
    {
        // The claim follows ITenantContext.Current at every open: once the scope is gone the next
        // connection carries nothing (RLS then denies), it never replays the old value.
        var tenantContext = new TenantContext();
        var interceptor = new TenantRlsConnectionInterceptor(tenantContext);
        var connection = new RecordingDbConnection();

        using (tenantContext.BeginScope(TenantId.New()))
        {
            await OpenAsync(interceptor, connection, sync);
        }

        await OpenAsync(interceptor, connection, sync);

        Assert.Single(connection.Commands);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task Close_resets_both_claims_in_one_command(bool sync)
    {
        var (interceptor, connection, scopes) = Arrange(TenantId.New(), "caller@example.com");
        using var _ = scopes;

        await CloseAsync(interceptor, connection, sync);

        var command = Assert.Single(connection.Commands);
        Assert.Equal(ResetText, command.CommandText);
        Assert.Equal(sync ? ExecutionPath.Sync : ExecutionPath.Async, command.Path);
        Assert.Empty(command.Parameters);
        Assert.True(command.Disposed);
    }

    [Theory]
    [MemberData(nameof(SyncAndAsync))]
    public async Task Close_resets_both_claims_even_when_no_scope_is_active_at_that_moment(bool sync)
    {
        // The ambient context can differ between open and close (or other code may have set a claim
        // on the same connection), so the reset never depends on what is "current" at close time.
        var (interceptor, connection, scopes) = Arrange(tenantId: null, identity: null);
        using var _ = scopes;

        await CloseAsync(interceptor, connection, sync);

        var command = Assert.Single(connection.Commands);
        Assert.Equal(ResetText, command.CommandText);
    }

    [Theory]
    [InlineData(true, ConnectionState.Closed)]
    [InlineData(true, ConnectionState.Broken)]
    [InlineData(true, ConnectionState.Connecting)]
    [InlineData(false, ConnectionState.Closed)]
    [InlineData(false, ConnectionState.Broken)]
    [InlineData(false, ConnectionState.Connecting)]
    public async Task Close_of_a_connection_that_is_not_open_runs_nothing(bool sync, ConnectionState state)
    {
        var (interceptor, connection, scopes) = Arrange(TenantId.New(), "caller@example.com");
        using var _ = scopes;
        connection.SetState(state);

        await CloseAsync(interceptor, connection, sync);

        Assert.Empty(connection.Commands);
    }

    [Fact]
    public async Task Interception_result_is_passed_through_unchanged_on_close()
    {
        var (interceptor, connection, scopes) = Arrange(TenantId.New(), identity: null);
        using var _ = scopes;
        var suppressed = InterceptionResult.Suppress();

        var sync = interceptor.ConnectionClosing(connection, null!, suppressed);
        var async = await interceptor.ConnectionClosingAsync(connection, null!, suppressed);

        Assert.True(sync.IsSuppressed);
        Assert.True(async.IsSuppressed);
    }

    private static (TenantRlsConnectionInterceptor Interceptor, RecordingDbConnection Connection, IDisposable Scopes)
        Arrange(TenantId? tenantId, string? identity)
    {
        var tenantContext = new TenantContext();
        var identityContext = new CallerIdentityContext();
        var scopes = new CompositeScope();
        if (tenantId is { } tenant)
        {
            scopes.Add(tenantContext.BeginScope(tenant));
        }

        if (identity is not null)
        {
            scopes.Add(identityContext.BeginIdentityScope(identity));
        }

        return (new TenantRlsConnectionInterceptor(tenantContext, identityContext), new RecordingDbConnection(), scopes);
    }

    private static async Task OpenAsync(TenantRlsConnectionInterceptor interceptor, RecordingDbConnection connection, bool sync)
    {
        if (sync)
        {
            interceptor.ConnectionOpened(connection, null!);
        }
        else
        {
            await interceptor.ConnectionOpenedAsync(connection, null!);
        }
    }

    private static async Task CloseAsync(TenantRlsConnectionInterceptor interceptor, RecordingDbConnection connection, bool sync)
    {
        if (sync)
        {
            interceptor.ConnectionClosing(connection, null!, default);
        }
        else
        {
            await interceptor.ConnectionClosingAsync(connection, null!, default);
        }
    }

    private enum ExecutionPath
    {
        Sync,
        Async,
    }

    private sealed class CompositeScope : IDisposable
    {
        private readonly List<IDisposable> _scopes = [];

        public void Add(IDisposable scope) => _scopes.Add(scope);

        public void Dispose()
        {
            for (var index = _scopes.Count - 1; index >= 0; index--)
            {
                _scopes[index].Dispose();
            }
        }
    }

    private sealed class FixedIdentityContext(string? current) : ICallerIdentityContext
    {
        public string? Current { get; } = current;

        public IDisposable BeginIdentityScope(string identity) => throw new NotSupportedException();
    }

    private sealed class RecordedCommand
    {
        public string CommandText { get; set; } = string.Empty;

        public List<(string Name, object? Value)> Parameters { get; } = [];

        public ExecutionPath Path { get; set; }

        public bool Disposed { get; set; }
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;

        public List<RecordedCommand> Commands { get; } = [];

        public void SetState(ConnectionState state) => _state = state;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new RecordingDbCommand(this);
    }

    private sealed class RecordingDbCommand(RecordingDbConnection owner) : DbCommand
    {
        private readonly RecordingParameterCollection _parameters = new();
        private readonly RecordedCommand _record = new();
        private bool _registered;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; } = owner;

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            Record(ExecutionPath.Sync);
            return -1;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            Record(ExecutionPath.Async);
            return Task.FromResult(-1);
        }

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _record.Disposed = true;
            base.Dispose(disposing);
        }

        private void Record(ExecutionPath path)
        {
            Assert.False(_registered, "A command must be executed at most once.");
            _registered = true;
            _record.CommandText = CommandText;
            _record.Path = path;
            _record.Parameters.AddRange(_parameters.Items.Select(p => (p.ParameterName, p.Value)));
            owner.Commands.Add(_record);
        }
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        public List<DbParameter> Items { get; } = [];

        public override int Count => Items.Count;

        public override object SyncRoot => ((ICollection)Items).SyncRoot;

        public override int Add(object value)
        {
            Items.Add((DbParameter)value);
            return Items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public override void Clear() => Items.Clear();

        public override bool Contains(object value) => Items.Contains((DbParameter)value);

        public override bool Contains(string value) => Items.Any(p => p.ParameterName == value);

        public override void CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => Items.GetEnumerator();

        public override int IndexOf(object value) => Items.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) => Items.FindIndex(p => p.ParameterName == parameterName);

        public override void Insert(int index, object value) => Items.Insert(index, (DbParameter)value);

        public override void Remove(object value) => Items.Remove((DbParameter)value);

        public override void RemoveAt(int index) => Items.RemoveAt(index);

        public override void RemoveAt(string parameterName) => Items.RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => Items[index];

        protected override DbParameter GetParameter(string parameterName) => Items[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => Items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => Items[IndexOf(parameterName)] = value;
    }
}
