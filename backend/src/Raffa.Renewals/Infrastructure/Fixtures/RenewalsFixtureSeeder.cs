using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Cases;
using Raffa.Renewals.Domain.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Renewals.Infrastructure.Fixtures;

/// <summary>
/// Fixture mode only (IMPLEMENTATION-PLAN.md §2): scenario <c>full</c> replays the prototype's
/// <c>seed()</c> (<c>cases-seed.json</c>: DocuSign counter-proposal to approve, Microsoft in the
/// interview, AWS waiting on a policy exception) through the real engine with the scripted fixture
/// services, and stores each case like a user-opened one. Scenario <c>zero</c> seeds no case.
/// </summary>
public sealed class RenewalsFixtureSeeder(
    RenewalsDbContext db,
    ITenantContext tenantContext,
    IContractFactsReader factsReader,
    ICaseMemberDirectory memberDirectory,
    RenewalCaseService caseService) : IFixtureScenarioSeeder
{
    public const string SeedFile = "cases-seed.json";

    public int Order => FixtureSeedOrder.Renewals;

    public Task WipeAsync(FixtureSeedContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(FixtureSeedContext context, string scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var seed = context.ReadJson<JsonObject>(SeedFile);
        if (!string.Equals(seed["scenario"]?.GetValue<string>(), scenario, StringComparison.Ordinal))
        {
            return;
        }

        var members = await memberDirectory.ListAsync(context.TenantId, cancellationToken).ConfigureAwait(false);
        string Name(string key) => context.User(key).DisplayName;

        foreach (var node in seed["cases"]!.AsArray())
        {
            var o = node!.AsObject();
            var fixtureId = o["fixtureContractId"]!.GetValue<int>();
            var contractId = FixtureIds.Contract(context.TenantId, fixtureId);
            var facts = await factsReader.GetAsync(context.TenantId, contractId, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Fixture contract {fixtureId} has no facts; the contracts seeder must run first.");
            var cf = CaseFactsMapper.ToCaseFacts(facts);
            CaseContext Ctx(string who) => new(cf, Name(who), context.Today, FixtureCaseScriptServices.Instance);

            var approver = RenewalCaseService.Approver(members) ?? Name("luca");
            var c = RenewalCaseEngine.Create(cf.Id, Ctx(o["createdBy"]!.GetValue<string>()), null, Name(o["owner"]!.GetValue<string>()), approver, members.Select(m => m.Name).ToList());
            foreach (var a in o["actions"]!.AsArray())
            {
                var step = a!.AsObject();
                var type = step["type"]!.GetValue<string>();
                var payload = step["payload"] is JsonObject p && p.Count > 0
                    ? p.Deserialize<CaseActionPayload>(RenewalCaseJson.Options) ?? CaseActionPayload.None
                    : CaseActionPayload.None;
                c = RenewalCaseEngine.Act(c, type, payload, Ctx(step["who"]!.GetValue<string>()));
            }

            c.Clip = null;
            c.Tick = false;
            c.Ui.Warn = null;
            if (o["updated"]?.GetValue<string>() is { } updated)
            {
                c.Updated = CaseFormat.ParseDate(updated);
            }

            using var scope = tenantContext.BeginScope(context.TenantId);
            var record = await caseService.InsertAsync(context.TenantId, contractId.Value, c, cf, members, Name(o["createdBy"]!.GetValue<string>()), cancellationToken).ConfigureAwait(false);
            foreach (var rep in c.Reps)
            {
                db.CaseVendorMessages.Add(new CaseVendorMessageRecord
                {
                    TenantId = context.TenantId,
                    CaseId = record.Id,
                    Direction = "in",
                    ReceivedAt = context.Now,
                    PastedText = rep.Text,
                    ContentHash = RenewalCaseService.Hash(rep.Text),
                    ParsedJson = JsonSerializer.Serialize(rep.Rows, RenewalCaseJson.Options),
                    AnalysedByModel = "fixture-script",
                    InputHash = RenewalCaseService.Hash(rep.Text),
                });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

public static class RenewalsFixtureServiceCollectionExtensions
{
    /// <summary>Fixture mode only: the scripted case services and the scenario seeder.</summary>
    public static IServiceCollection AddRenewalsFixtureSeeder(this IServiceCollection services)
    {
        services.AddSingleton<ICaseScriptServices>(FixtureCaseScriptServices.Instance);
        services.AddScoped<IFixtureScenarioSeeder, RenewalsFixtureSeeder>();
        return services;
    }
}
