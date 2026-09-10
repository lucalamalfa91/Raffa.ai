using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Tenancy.Tests;

/// <summary>
/// Unit-level proof of <see cref="TenantContext"/>'s ambient-scope mechanics, independent of any
/// database. The RLS/interceptor tests in this project depend on this behaving correctly
/// (fail-closed when no scope is active, correct nesting/restore, and per-async-flow isolation)
/// so it is proven in isolation first.
/// </summary>
public sealed class TenantContextTests
{
    [Fact]
    public void No_active_scope_yields_null_current()
    {
        ITenantContext context = new TenantContext();

        Assert.Null(context.Current);
    }

    [Fact]
    public void BeginScope_sets_current_for_the_scope_duration()
    {
        ITenantContext context = new TenantContext();
        var tenantId = TenantId.New();

        using (context.BeginScope(tenantId))
        {
            Assert.Equal(tenantId, context.Current);
        }

        Assert.Null(context.Current);
    }

    [Fact]
    public void Disposing_a_scope_restores_the_previous_value()
    {
        ITenantContext context = new TenantContext();
        var outerTenant = TenantId.New();
        var innerTenant = TenantId.New();

        using (context.BeginScope(outerTenant))
        {
            Assert.Equal(outerTenant, context.Current);

            using (context.BeginScope(innerTenant))
            {
                Assert.Equal(innerTenant, context.Current);
            }

            // Nested scope's dispose must restore the outer tenant, not clear it.
            Assert.Equal(outerTenant, context.Current);
        }

        Assert.Null(context.Current);
    }

    [Fact]
    public void Disposing_a_scope_twice_is_a_harmless_no_op()
    {
        ITenantContext context = new TenantContext();
        var outerTenant = TenantId.New();
        var innerTenant = TenantId.New();

        using var outer = context.BeginScope(outerTenant);
        var inner = context.BeginScope(innerTenant);

        inner.Dispose();
        inner.Dispose(); // must not re-pop and clobber the outer tenant a second time.

        Assert.Equal(outerTenant, context.Current);
    }

    [Fact]
    public async Task Concurrent_async_flows_never_see_each_others_tenant()
    {
        ITenantContext context = new TenantContext();

        async Task<TenantId?> RunInScope(TenantId tenantId)
        {
            using (context.BeginScope(tenantId))
            {
                // Yield so the two flows genuinely interleave rather than running sequentially.
                await Task.Delay(10);
                return context.Current;
            }
        }

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        var taskA = RunInScope(tenantA);
        var taskB = RunInScope(tenantB);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal(tenantA, results[0]);
        Assert.Equal(tenantB, results[1]);
    }
}
