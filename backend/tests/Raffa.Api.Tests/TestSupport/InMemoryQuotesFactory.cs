using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Swaps <see cref="QuotesDbContext"/> for the EF Core InMemory provider (task E25/F04/US01/T01,
/// quote-benchmark-backend) -- the same "RemoveAll then AddDbContext.UseInMemoryDatabase" shape
/// <see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/> already establishes for
/// <c>DocumentsContractsDbContext</c>/<c>ChatDbContext</c>/<c>IdentityWorkspaceDbContext</c>/
/// <c>RenewalsDbContext</c>, reusing that same helper's shared
/// <see cref="InMemoryAskEngineFactory.InMemoryProviderServices"/> internal service provider (its own
/// doc comment: "generic machinery, not tied to a specific database name").
///
/// <para>
/// A dedicated, Quotes-only swap rather than widening <see cref="InMemoryAskEngineFactory"/> itself:
/// no scenario that shared helper already serves (Ask/Documents/Chat/Renewals) touches
/// <c>Raffa.Quotes</c>, so adding this module there would be an unrelated-caller risk for every one
/// of its existing callers rather than a shared concern.
/// </para>
///
/// <para>
/// <see cref="QuotesDbContext"/> has no Postgres-specific column type (no pgvector, unlike
/// <c>Raffa.Documents.Contracts.Domain.Embedding.Vector</c> — see
/// <see cref="InMemoryModelCustomizer"/>'s own doc comment), so this swap needs no model
/// customization of its own — the InMemory provider's default <c>IModelCustomizer</c> is enough.
/// </para>
///
/// <para>
/// Real Postgres+RLS coverage for this module's services lives in
/// <c>Raffa.Quotes.Tests</c> (Testcontainers — see e.g. <c>QuoteQueryServiceTests</c>,
/// <c>MarketAssessmentServiceTests</c>); this swap exists so this project can additionally prove a
/// real HTTP round trip through <c>Program.cs</c> (routing, the NW-05 caller ladder, wire shaping)
/// without a running Postgres — the same division of labour <see cref="InMemoryAskEngineFactory"/>'s
/// own doc comment describes for its own two swapped contexts.
/// </para>
/// </summary>
internal static class InMemoryQuotesFactory
{
    public static WebApplicationFactory<Program> WithInMemoryQuotesDb(
        this WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var quotesDbName = $"quotes-{Guid.NewGuid()}";

        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // AddDbContext's own core-services registration is TryAdd (first registration wins) --
            // see InMemoryAskEngineFactory's own doc comment for why the existing Npgsql-configured
            // registration must be removed first, or this swap would be silently ignored and every
            // query would still try (and fail/hang) against the real, unreachable Postgres host
            // Program.cs's own connection string names.
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<QuotesDbContext>();
            services.AddDbContext<QuotesDbContext>(o => o
                .UseInMemoryDatabase(quotesDbName)
                .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));
        }));
    }

    /// <summary>Writes <paramref name="quote"/> straight into the InMemory <see cref="QuotesDbContext"/>
    /// this factory's own <see cref="WithInMemoryQuotesDb"/> just wired -- same "resolve the real
    /// service from the host's own container, skip HTTP" shape
    /// <see cref="InMemoryAskEngineFactory.SeedContractAsync"/> already uses, since there is no
    /// lightweight HTTP path to create a <see cref="Quote"/> row without a real extraction
    /// pipeline/AI Gateway/blob storage round trip.</summary>
    public static async Task SeedQuoteAsync(this WebApplicationFactory<Program> factory, Quote quote)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(quote);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        dbContext.Quotes.Add(quote);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>Same, for one <see cref="QuoteLine"/> on an already-seeded quote.</summary>
    public static async Task SeedQuoteLineAsync(this WebApplicationFactory<Program> factory, QuoteLine line)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(line);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        dbContext.QuoteLines.Add(line);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }
}
