using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application;
using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Chat.Tests;

/// <summary>
/// Proves task E02/F04/US02/T01's own wiring claim (mirrors
/// <c>Raffa.AiGateway.Tests.ServiceCollectionExtensionsTests</c>): <see cref="AskRaffaQueryRouter"/>,
/// <see cref="DeterministicQueryPlanner"/>, <see cref="DeterministicQueryHandler"/>,
/// <see cref="AbstainGuard"/> (task E02/F04/US02/T02) and <see cref="RagAnswerService"/> are all
/// resolvable from a container that has
/// <see cref="AddChatModule"/> plus this module's two external dependencies
/// (<see cref="IAiGateway"/>, <see cref="IAuditWriter"/>) registered — the shape
/// <c>Raffa.Api.Program</c>'s real composition already provides via
/// <c>AddDocumentsContractsModule</c>/<c>AddAuditModule</c>.
///
/// <see cref="ServiceProviderOptions.ValidateOnBuild"/> + <see cref="ServiceProviderOptions.ValidateScopes"/>
/// (both <see langword="true"/> below) is the actual proof behind
/// <c>Infrastructure.ServiceCollectionExtensions</c>'s own doc comment: if
/// <see cref="RagAnswerService"/> had been registered Singleton instead of Scoped, building this
/// provider would throw ("Cannot consume scoped service ... from singleton ...") because it
/// depends on a Scoped <see cref="IAuditWriter"/> — this test fails loudly if that regresses.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddChatModule_resolves_every_module_service_with_no_captive_dependency()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAiGateway, NotExercisedGateway>();
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();

        services.AddChatModule();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AskRaffaQueryRouter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DeterministicQueryPlanner>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DeterministicQueryHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AbstainGuard>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RagAnswerService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddChatModule_does_not_override_an_already_registered_IClock()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAiGateway, NotExercisedGateway>();
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();
        var preRegisteredClock = new FixedTimeClock();
        services.AddSingleton<IClock>(preRegisteredClock);

        services.AddChatModule();

        using var provider = services.BuildServiceProvider();

        // TryAddSingleton: the first registration wins — same defensive convention every other
        // module's own AddXxxModule already uses.
        Assert.Same(preRegisteredClock, provider.GetRequiredService<IClock>());
    }

    /// <summary>
    /// Task E13/F05/US01/T01 (story us-01-conversations, AC-4 "...and registers the DbContext
    /// when one is given"): the other half of <see cref="AddChatModule"/>'s own doc comment on
    /// the new <c>chatConnectionString</c> overload. A syntactically-valid-but-never-dialled
    /// connection string is enough — <c>AddDbContext</c> registration is lazy (the provider is
    /// never actually opened just by building/validating the container), the same "by design"
    /// convention this codebase's own
    /// <c>Raffa.Renewals.Tests.RenewalThresholdSchedulerHostedServiceTests</c> already uses.
    /// </summary>
    [Fact]
    public void AddChatModule_with_a_connection_string_also_resolves_ChatDbContext_and_ConversationService()
    {
        const string neverDialledConnectionString =
            "Host=localhost;Database=never_dialled;Username=x;Password=x";

        var services = new ServiceCollection();
        services.AddSingleton<IAiGateway, NotExercisedGateway>();
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();

        services.AddChatModule(neverDialledConnectionString);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Raffa.Chat.Infrastructure.ChatDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Raffa.Chat.Application.Conversations.ConversationService>());
        // The zero-argument surface from the other test above still resolves too -- the overload
        // is additive, never a replacement.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AskRaffaQueryRouter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RagAnswerService>());
    }

    private sealed class FixedTimeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Never actually invoked by this test — only its resolvability matters — so every
    /// method throws rather than returning a plausible-looking default.</summary>
    private sealed class NotExercisedGateway : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
