using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Renewals;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>No quote module wired: no quote decisions in Today (the web app also reads checked quotes itself).</summary>
public sealed class NoOpenQuoteDecisions : IOpenQuoteDecisions
{
    public Task<IReadOnlyList<OpenQuoteDecision>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OpenQuoteDecision>>([]);
}

/// <summary>No documents module wired: no review items in Today.</summary>
public sealed class NoDocumentsNeedingReview : IDocumentsNeedingReview
{
    public Task<IReadOnlyList<DocumentNeedingReview>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DocumentNeedingReview>>([]);
}

public static class RenewalCaseServiceCollectionExtensions
{
    /// <summary>
    /// The AskRaffa 4.0 case services. The Renewals module is the real implementation of the case
    /// read-models other slices declared (<see cref="IContractCaseStatusReader"/>,
    /// <see cref="ICaseEmailDraftSink"/>, <see cref="ICaseGoalsReader"/>): those replace the defaults.
    /// Writers it calls (contract terms, objectives, saving results, the composer answerer) come from
    /// the modules that own them.
    /// </summary>
    public static IServiceCollection AddRenewalCases(this IServiceCollection services)
    {
        services.TryAddSingleton<ICaseScriptServices, ProductionCaseScriptServices>();
        services.TryAddScoped<ICaseAiReader, NoCaseAiReader>();
        services.TryAddScoped<IOpenQuoteDecisions, NoOpenQuoteDecisions>();
        services.TryAddScoped<IDocumentsNeedingReview, NoDocumentsNeedingReview>();
        services.TryAddScoped<IContractSituationAssessor, RulesContractSituationAssessor>();
        services.TryAddSingleton<ICompanyRulesAssistant, ScriptedCompanyRulesAssistant>();
        services.AddScoped<CompanyRulesService>();
        services.AddScoped<RenewalCaseService>();
        services.Replace(ServiceDescriptor.Scoped<IContractCaseStatusReader>(sp => sp.GetRequiredService<RenewalCaseService>()));
        services.Replace(ServiceDescriptor.Scoped<ICaseEmailDraftSink>(sp => sp.GetRequiredService<RenewalCaseService>()));
        services.Replace(ServiceDescriptor.Scoped<ICaseGoalsReader>(sp => sp.GetRequiredService<RenewalCaseService>()));
        return services;
    }
}
