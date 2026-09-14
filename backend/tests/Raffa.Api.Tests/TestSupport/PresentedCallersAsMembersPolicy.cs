using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Fix 2026-09-14 (NW-05 on the data plane). Marker a test class opts into with
/// <see cref="PresentedCallersAsMembersExtensions.WithPresentedCallersAsMembers"/>: on that host,
/// <see cref="ImplicitTenantAdminStartupFilter"/> also grants an Admin membership to a caller the
/// request DOES present (<c>X-User-Id</c>) in the tenant it names. The data-plane endpoint classes
/// (chat, conversations, upload) send a fixed address on a fresh tenant per test and never meant
/// to test membership -- before NW-05 there was none to test. Authorization classes never opt in:
/// for them a presented non-member must stay a 404, and it does.
/// </summary>
public sealed class PresentedCallersAsMembersPolicy
{
}

public static class PresentedCallersAsMembersExtensions
{
    public static WebApplicationFactory<Program> WithPresentedCallersAsMembers(this WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(new PresentedCallersAsMembersPolicy())));
    }
}
