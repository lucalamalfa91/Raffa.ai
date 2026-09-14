using Raffa.Identity.Workspace.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Fix 2026-09-14 (NW-05 applied to the data plane). The one <see cref="WebApplicationFactory{T}"/>
/// every endpoint test class in this project shares as its <c>IClassFixture</c>, so that every host
/// derived from it -- including the ones a class builds with <c>WithWebHostBuilder</c>, which copy
/// this <see cref="ConfigureWebHost"/> -- carries the two test-only pieces the token gate needs:
/// <list type="bullet">
/// <item><see cref="TestUserIdAuthenticationHandler"/>: the <c>X-User-Id</c> header these tests
/// already send becomes the authenticated <c>oid</c> (and <c>email</c>) claim the production seam
/// reads. Before this, eleven classes built their hosts straight off the base factory and never
/// saw the bridge <c>InMemoryAskEngineFactory</c> installed, so with the gate on every one of their
/// requests was anonymous and answered 401.</item>
/// <item><see cref="ImplicitTenantAdminStartupFilter"/>: a request that names a tenant but no caller
/// runs as that tenant's implicit Admin -- the pre-NW-05 posture these tests were written against.
/// Never touches a request that presents a caller.</item>
/// </list>
/// A test that must prove the gate itself constructs
/// <c>new RaffaApiFactory { ImplicitTenantAdmin = false }</c>: no caller then means 401 everywhere.
/// <c>Raffa.Api.Program</c> never sees either type.
/// </summary>
public sealed class RaffaApiFactory : WebApplicationFactory<Program>
{
    /// <summary>xunit allows a class fixture exactly one public constructor, so the opt-out is an
    /// init-only property: <c>new RaffaApiFactory { ImplicitTenantAdmin = false }</c>. The host is
    /// built lazily on first use, so the value is read in time.</summary>
    public bool ImplicitTenantAdmin { get; init; } = true;

    private readonly string _identityDbName = $"identity-workspace-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // ICallerContext verifies membership in IdentityWorkspaceDbContext on every guarded
            // request. The classes that swap only their own module's DbContext used to leave this
            // one pointed at appsettings' never-reachable Postgres (127.0.0.1:5432 refused); with
            // the gate on, every one of their requests would dial it. So the base factory swaps it
            // to an in-memory store for every host, unique per factory instance;
            // InMemoryAskEngineFactory's own swap of the same context is then a harmless re-swap.
            services.RemoveAll<DbContextOptions<IdentityWorkspaceDbContext>>();
            services.RemoveAll<IdentityWorkspaceDbContext>();
            services.AddDbContext<IdentityWorkspaceDbContext>(o => o
                .UseInMemoryDatabase(_identityDbName)
                .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));

            services.AddAuthentication(TestUserIdAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestUserIdAuthenticationHandler>(
                    TestUserIdAuthenticationHandler.SchemeName, _ => { });

            if (ImplicitTenantAdmin)
            {
                services.AddSingleton<IStartupFilter, ImplicitTenantAdminStartupFilter>();
            }
        });
    }
}
