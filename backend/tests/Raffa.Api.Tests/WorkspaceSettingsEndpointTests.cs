using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Chat.Application.WebResearch;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// ADR-030 gate 2 over the real HTTP pipeline: `GET`/`PATCH /api/workspaces/{tenantId}/settings`.
/// Same ladder as the members route group (401 -> 404 for a non-member -> any member reads ->
/// only an Admin writes, 403 otherwise), and a change writes one audit row.
/// </summary>
public sealed class WorkspaceSettingsEndpointTests : IClassFixture<RaffaApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _baseFactory;

    public WorkspaceSettingsEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task No_identity_returns_401()
    {
        using var bare = new RaffaApiFactory { ImplicitTenantAdmin = false };
        var factory = bare.WithInMemoryAskEngine(NewGateway());
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_non_member_gets_404_on_read_and_write()
    {
        var factory = WithInMemoryIdentity(new RecordingAuditWriter());
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedWorkspaceAsync(factory, tenantId);

        var read = await SendAsync(client, HttpMethod.Get, tenantId, "stranger@acme.example");
        var write = await SendAsync(client, HttpMethod.Patch, tenantId, "stranger@acme.example", new { webResearchEnabled = true });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task Any_member_reads_the_switch_which_defaults_to_off()
    {
        var factory = WithInMemoryIdentity(new RecordingAuditWriter());
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedWorkspaceAsync(factory, tenantId);
        await SeedMemberAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);

        var response = await SendAsync(client, HttpMethod.Get, tenantId, "buyer@acme.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("webResearchEnabled").GetBoolean());
        Assert.False(body.RootElement.GetProperty("canEdit").GetBoolean());
        // ADR-032: Program's default kill switch is off, so Ask shows no web-search toggle.
        Assert.False(body.RootElement.GetProperty("webResearchAvailable").GetBoolean());
    }

    [Fact]
    public async Task The_environments_kill_switch_is_published_as_web_research_available()
    {
        var factory = WithInMemoryIdentity(new RecordingAuditWriter()).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<WebResearchOptions>();
                services.AddSingleton(new WebResearchOptions { Enabled = true });
            }));
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedWorkspaceAsync(factory, tenantId);
        await SeedMemberAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);

        var response = await SendAsync(client, HttpMethod.Get, tenantId, "buyer@acme.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("webResearchAvailable").GetBoolean());
        Assert.False(body.RootElement.GetProperty("webResearchEnabled").GetBoolean());
    }

    [Fact]
    public async Task Only_an_admin_flips_the_switch_and_the_change_is_audited()
    {
        var audit = new RecordingAuditWriter();
        var factory = WithInMemoryIdentity(audit);
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedWorkspaceAsync(factory, tenantId);
        await SeedMemberAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin);
        await SeedMemberAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);

        var forbidden = await SendAsync(client, HttpMethod.Patch, tenantId, "buyer@acme.example", new { webResearchEnabled = true });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var empty = await SendAsync(client, HttpMethod.Patch, tenantId, "admin@acme.example", new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var enabled = await SendAsync(client, HttpMethod.Patch, tenantId, "admin@acme.example", new { webResearchEnabled = true });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using var enabledBody = JsonDocument.Parse(await enabled.Content.ReadAsStringAsync());
        Assert.True(enabledBody.RootElement.GetProperty("webResearchEnabled").GetBoolean());
        Assert.True(enabledBody.RootElement.GetProperty("canEdit").GetBoolean());

        var read = await SendAsync(client, HttpMethod.Get, tenantId, "buyer@acme.example");
        using var readBody = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.True(readBody.RootElement.GetProperty("webResearchEnabled").GetBoolean());

        var entry = Assert.Single(audit.Entries, e => e.Action == WorkspaceSettingsService.AuditWebResearchEnabledAction);
        Assert.Equal("admin@acme.example", entry.Actor);
        Assert.Equal("enabled=True", entry.Detail);

        // Setting the same value again is a no-op: no second audit row.
        var again = await SendAsync(client, HttpMethod.Patch, tenantId, "admin@acme.example", new { webResearchEnabled = true });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(audit.Entries, e => e.Action == WorkspaceSettingsService.AuditWebResearchEnabledAction);
    }

    [Fact]
    public async Task An_admin_sets_currency_fiscal_year_and_budget_rates_each_audited_and_any_member_reads_them()
    {
        var audit = new RecordingAuditWriter();
        var factory = WithInMemoryIdentity(audit);
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedWorkspaceAsync(factory, tenantId);
        await SeedMemberAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin);
        await SeedMemberAsync(factory, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);

        var before = await SendAsync(client, HttpMethod.Get, tenantId, "buyer@acme.example");
        using (var body = JsonDocument.Parse(await before.Content.ReadAsStringAsync()))
        {
            Assert.Equal("CHF", body.RootElement.GetProperty("reportingCurrency").GetString());
            Assert.False(body.RootElement.GetProperty("reportingCurrencySet").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("fiscalYearStartMonth").ValueKind);
            Assert.Contains("EUR", body.RootElement.GetProperty("supportedCurrencies").EnumerateArray().Select(c => c.GetString()));
        }

        var forbidden = await SendAsync(client, HttpMethod.Patch, tenantId, "buyer@acme.example", new { fiscalYearStartMonth = 4 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var set = await SendAsync(client, HttpMethod.Patch, tenantId, "admin@acme.example", new
        {
            reportingCurrency = "chf",
            fiscalYearStartMonth = 4,
            budgetRates = new Dictionary<string, decimal> { ["eur"] = 0.95m, ["GBP"] = 1.12m, ["CHF"] = 1m },
        });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var read = await SendAsync(client, HttpMethod.Get, tenantId, "buyer@acme.example");
        using (var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync()))
        {
            Assert.Equal("CHF", body.RootElement.GetProperty("reportingCurrency").GetString());
            Assert.True(body.RootElement.GetProperty("reportingCurrencySet").GetBoolean());
            Assert.Equal(4, body.RootElement.GetProperty("fiscalYearStartMonth").GetInt32());
            var rates = body.RootElement.GetProperty("budgetRates");
            Assert.Equal(0.95m, rates.GetProperty("EUR").GetDecimal());
            Assert.Equal(1.12m, rates.GetProperty("GBP").GetDecimal());
            Assert.False(rates.TryGetProperty("CHF", out _)); // no rate for the reporting currency itself
        }

        Assert.Single(audit.Entries, e => e.Action == WorkspaceSettingsService.AuditReportingCurrencyAction && e.Detail == "currency=CHF");
        Assert.Single(audit.Entries, e => e.Action == WorkspaceSettingsService.AuditFiscalYearStartMonthAction && e.Detail == "month=4");
        Assert.Single(audit.Entries, e => e.Action == WorkspaceSettingsService.AuditBudgetRatesAction && e.Detail == "rates=EUR:0.95,GBP:1.12");

        // Spesa e risparmi reads the same settings.
        using var scope = factory.Services.CreateScope();
        var finance = await scope.ServiceProvider.GetRequiredService<Raffa.SharedKernel.Savings.IWorkspaceFinanceSettingsReader>()
            .GetAsync(new TenantId(tenantId));
        Assert.Equal("CHF", finance.ReportingCurrency);
        Assert.Equal(4, finance.FiscalYearStartMonth);
        Assert.Equal(0.95m, finance.RateFor("EUR"));
        Assert.Null(finance.RateFor("USD"));
    }

    [Theory]
    [InlineData("""{"fiscalYearStartMonth":13}""")]
    [InlineData("""{"fiscalYearStartMonth":0}""")]
    [InlineData("""{"reportingCurrency":"XYZ"}""")]
    [InlineData("""{"budgetRates":{"EUR":0}}""")]
    [InlineData("""{"budgetRates":{"EUR":-1}}""")]
    [InlineData("""{"budgetRates":{"XYZ":1.1}}""")]
    [InlineData("""{"budgetRates":{"EUR":1.1,"eur":1.2}}""")]
    public async Task An_invalid_finance_setting_is_400_and_changes_nothing(string json)
    {
        var audit = new RecordingAuditWriter();
        var factory = WithInMemoryIdentity(audit);
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedWorkspaceAsync(factory, tenantId);
        await SeedMemberAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/workspaces/{tenantId}/settings")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-User-Id", "admin@acme.example");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(audit.Entries, e => e.Action.StartsWith("workspace.settings.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changing_the_reporting_currency_clears_rates_that_were_relative_to_the_old_one()
    {
        var audit = new RecordingAuditWriter();
        var factory = WithInMemoryIdentity(audit);
        var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        await SeedWorkspaceAsync(factory, tenantId);
        await SeedMemberAsync(factory, tenantId, "admin@acme.example", WorkspaceRoleName.Admin);

        await SendAsync(client, HttpMethod.Patch, tenantId, "admin@acme.example", new { budgetRates = new Dictionary<string, decimal> { ["EUR"] = 0.95m } });
        var changed = await SendAsync(client, HttpMethod.Patch, tenantId, "admin@acme.example", new { reportingCurrency = "EUR" });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var body = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.Equal("EUR", body.RootElement.GetProperty("reportingCurrency").GetString());
        Assert.Empty(body.RootElement.GetProperty("budgetRates").EnumerateObject());
        Assert.Single(audit.Entries, e => e.Action == WorkspaceSettingsService.AuditBudgetRatesAction && (e.Detail ?? "").Contains("cleared", StringComparison.Ordinal));
    }

    // ----- helpers -----

    private static RecordingAiGateway NewGateway() => new(
        new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

    private WebApplicationFactory<Program> WithInMemoryIdentity(RecordingAuditWriter audit) =>
        _baseFactory.WithInMemoryAskEngine(NewGateway(), clock: new FixedClock(Now), auditWriter: audit);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, Guid tenantId, string callerUserId, object? body = null)
    {
        using var request = new HttpRequestMessage(method, $"/api/workspaces/{tenantId}/settings");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-User-Id", callerUserId);
        return await client.SendAsync(request);
    }

    private static async Task SeedWorkspaceAsync(WebApplicationFactory<Program> factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        db.Workspaces.Add(new WorkspaceTenant
        {
            Id = new EntityId(tenantId),
            TenantId = new TenantId(tenantId),
            Name = "Acme",
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedMemberAsync(
        WebApplicationFactory<Program> factory, Guid tenantId, string email, WorkspaceRoleName roleName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var user = new WorkspaceUser { TenantId = tenant, Email = email, ExternalSubjectId = email, CreatedAt = Now };
        var role = new WorkspaceRole { TenantId = tenant, Name = roleName, CreatedAt = Now };
        db.WorkspaceUsers.Add(user);
        db.WorkspaceRoles.Add(role);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = tenant,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();
    }
}
