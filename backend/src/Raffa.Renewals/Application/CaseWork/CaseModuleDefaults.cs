using Microsoft.Extensions.Configuration;
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
        services.TryAddScoped<Raffa.SharedKernel.Market.IMarketObservationSink, Raffa.SharedKernel.Market.NoMarketObservationSink>();
        services.TryAddScoped<IOpenQuoteDecisions, NoOpenQuoteDecisions>();
        services.TryAddScoped<IDocumentsNeedingReview, NoDocumentsNeedingReview>();
        services.TryAddScoped<IContractSituationAssessor, RulesContractSituationAssessor>();
        services.TryAddSingleton<ICompanyRulesAssistant, ScriptedCompanyRulesAssistant>();
        services.AddScoped<CompanyRulesService>();
        services.AddScoped<RenewalCaseService>();

        // Renewal plan F2/F3: company mailboxes, case addresses, inbound mail and notifications. Off until
        // configured (CaseMail__*, Notifications__*); the host adds the mailbox providers and the dispatcher.
        services.TryAddSingleton(sp =>
        {
            var options = new Raffa.Renewals.Application.Outreach.CaseMailOptions();
            sp.GetService<IConfiguration>()?.GetSection(Raffa.Renewals.Application.Outreach.CaseMailOptions.SectionName).Bind(options);
            return options;
        });
        services.TryAddSingleton(sp =>
        {
            var options = new Raffa.Renewals.Application.Outreach.NotificationOptions();
            sp.GetService<IConfiguration>()?.GetSection(Raffa.Renewals.Application.Outreach.NotificationOptions.SectionName).Bind(options);
            return options;
        });
        services.TryAddSingleton<Raffa.Renewals.Application.Notifications.INotificationDispatcher, Raffa.Renewals.Application.Notifications.NullNotificationDispatcher>();
        services.AddScoped<Raffa.Renewals.Application.Notifications.NotificationService>();
        services.AddScoped<Raffa.Renewals.Application.Outreach.MailboxService>();
        services.AddScoped<Raffa.Renewals.Application.Outreach.CaseMailService>();
        services.Replace(ServiceDescriptor.Scoped<IContractCaseStatusReader>(sp => sp.GetRequiredService<RenewalCaseService>()));
        services.Replace(ServiceDescriptor.Scoped<ICaseEmailDraftSink>(sp => sp.GetRequiredService<RenewalCaseService>()));
        services.Replace(ServiceDescriptor.Scoped<ICaseGoalsReader>(sp => sp.GetRequiredService<RenewalCaseService>()));
        return services;
    }
}
