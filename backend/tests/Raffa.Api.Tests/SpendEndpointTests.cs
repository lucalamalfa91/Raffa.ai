using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.Api.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof that <c>GET /api/savings/spend</c> (Spesa e risparmi) is mapped, guards its
/// query, and returns the contracts' value per fiscal quarter with the ones it cannot value listed
/// apart. The contract readers and the workspace settings are fakes, so no Postgres is needed.
/// </summary>
public sealed class SpendEndpointTests : IClassFixture<RaffaApiFactory>
{
    private static readonly Guid Chf = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Eur = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly WebApplicationFactory<Program> _factory;

    public SpendEndpointTests(RaffaApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IContractFactsReader>();
            services.RemoveAll<IContractTermHistoryReader>();
            services.RemoveAll<IWorkspaceFinanceSettingsReader>();
            services.AddSingleton<IContractFactsReader, FakeFacts>();
            services.AddSingleton<IContractTermHistoryReader, FakeHistory>();
            services.AddSingleton<IWorkspaceFinanceSettingsReader, FakeSettings>();
        }));
    }

    [Theory]
    [InlineData("?basis=estimate")]
    [InlineData("?from=2026")]
    [InlineData("?from=2028&to=2026")]
    [InlineData("?from=2020&to=2030")]
    [InlineData("?from=1980&to=1981")]
    public async Task An_invalid_query_returns_400(string query)
    {
        var response = await SendAsync("/api/savings/spend" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_report_values_documented_terms_and_lists_a_currency_without_a_rate()
    {
        var response = await SendAsync("/api/savings/spend?from=2024&to=2024&basis=accrual");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("CHF", root.GetProperty("currency").GetString());
        Assert.Equal("accrual", root.GetProperty("basis").GetString());
        Assert.Equal(4, root.GetProperty("periods").GetArrayLength());
        Assert.Equal("Q1 2024", root.GetProperty("periods")[0].GetProperty("label").GetString());

        var row = Assert.Single(root.GetProperty("contracts").EnumerateArray());
        Assert.Equal(Chf, row.GetProperty("contractId").GetGuid());
        // 2024 is a leap year: 366 days of a 366'000 contract year, all of it in the past.
        Assert.Equal(366_000m, row.GetProperty("actual").EnumerateArray().Sum(v => v.GetDecimal()));

        var excluded = Assert.Single(root.GetProperty("excluded").EnumerateArray());
        Assert.Equal(Eur, excluded.GetProperty("contractId").GetGuid());
        Assert.Equal("noRate", excluded.GetProperty("reason").GetString());
        Assert.Equal("EUR", excluded.GetProperty("currency").GetString());
    }

    private async Task<HttpResponseMessage> SendAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Tenant-Id", TenantId.New().Value.ToString());
        return await _factory.CreateClient().SendAsync(request);
    }

    private sealed class FakeFacts : IContractFactsReader
    {
        public Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContractFacts>>(
            [
                new ContractFacts { ContractId = Chf, Supplier = "Acme", Name = "Acme MSA", Currency = "CHF", Auto = "No", Spend = 366_000m },
                new ContractFacts { ContractId = Eur, Supplier = "Euro", Name = "Euro MSA", Currency = "EUR", Auto = "No", Spend = 100_000m },
            ]);

        public Task<ContractFacts?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ContractFacts?>(null);
    }

    private sealed class FakeHistory : IContractTermHistoryReader
    {
        public Task<IReadOnlyDictionary<Guid, ContractTermHistory>> ListAsync(
            TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken = default)
        {
            DocumentedContractTerm Term(decimal annual, string currency) =>
                new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), annual, null, currency, DocumentedTermSource.Contract, DateTimeOffset.UnixEpoch);
            return Task.FromResult<IReadOnlyDictionary<Guid, ContractTermHistory>>(new Dictionary<Guid, ContractTermHistory>
            {
                [Chf] = new(Chf, [Term(366_000m, "CHF")], null),
                [Eur] = new(Eur, [Term(100_000m, "EUR")], null),
            });
        }
    }

    private sealed class FakeSettings : IWorkspaceFinanceSettingsReader
    {
        public Task<WorkspaceFinanceSettings> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkspaceFinanceSettings.Default);
    }
}
