using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>Until slice 4 writes renewed terms, closing a case writes nothing outside the case.</summary>
public sealed class NoContractTermWriter : IContractTermWriter
{
    public Task WriteRenewedTermsAsync(TenantId tenantId, RenewedContractTerms terms, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Until slice 5 records saving results, a signed renewal records none.</summary>
public sealed class NoSavingResultWriter : ISavingResultWriter
{
    public Task RecordAsync(TenantId tenantId, SavingResultRecord result, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>No quote module wired: no quote decisions in Today.</summary>
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
    /// The AskRaffa 4.0 case services. Every cross-module port is <c>TryAdd</c>ed with an honest default
    /// so the module that owns the real implementation (Contracts, Savings, Quotes, Chat) may register
    /// its own, before or after this call (a later plain <c>Add</c> wins at resolution).
    /// </summary>
    public static IServiceCollection AddRenewalCases(this IServiceCollection services)
    {
        services.TryAddSingleton<ICaseScriptServices, ProductionCaseScriptServices>();
        services.TryAddScoped<ICaseQuestionAnswerer, ContractFactsQuestionAnswerer>();
        services.TryAddScoped<IContractTermWriter, NoContractTermWriter>();
        services.TryAddScoped<ISavingResultWriter, NoSavingResultWriter>();
        services.TryAddScoped<IOpenQuoteDecisions, NoOpenQuoteDecisions>();
        services.TryAddScoped<IDocumentsNeedingReview, NoDocumentsNeedingReview>();
        services.AddScoped<RenewalCaseService>();
        services.TryAddScoped<IContractCaseStatusReader>(sp => sp.GetRequiredService<RenewalCaseService>());
        services.TryAddScoped<ICaseObjectiveSource>(sp => sp.GetRequiredService<RenewalCaseService>());
        services.TryAddScoped<ICaseEmailDraftSink>(sp => sp.GetRequiredService<RenewalCaseService>());
        return services;
    }
}
