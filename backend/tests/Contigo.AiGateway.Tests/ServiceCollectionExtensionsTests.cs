using System.Reflection;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Fixtures;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Logging;
using Contigo.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Contigo.AiGateway.Tests;

/// <summary>
/// Proves task E02/F01/US02/T01's original wiring claim plus task E13/F01/US01/T02's DI-swap
/// rewrite: <see cref="AiGatewayModelOptions"/>/<see cref="AiGatewayOcrOptions"/> resolve from DI
/// with ADR-004/ADR-017 defaults (unchanged by this task), and <see cref="IAiGateway"/> now always
/// resolves to <see cref="LoggingAiGateway"/> — wrapping <see cref="FixtureAiGateway"/> when
/// <c>AiGateway:Endpoint</c> is unset, <see cref="FoundryAiGateway"/> when it is set.
///
/// <see cref="ServiceProviderOptions.ValidateOnBuild"/> + <see cref="ServiceProviderOptions.ValidateScopes"/>
/// (both <see langword="true"/> in the DI-swap tests below) is the same captive-dependency proof
/// <c>Contigo.Chat.Tests.ServiceCollectionExtensionsTests</c> already uses: if
/// <see cref="IAiGateway"/> had been left/regressed to Singleton, building this provider would
/// throw ("Cannot consume scoped service ... from singleton ...") because
/// <see cref="LoggingAiGateway"/> depends on the Scoped <see cref="IAuditWriter"/> — this fails
/// loudly if that regresses. The endpoint-set test never invokes a role method on the resolved
/// gateway — only <see cref="GetInnerGateway"/>'s reflection read — so constructing
/// <see cref="FoundryAiGateway"/>'s dependency graph never performs the one operation that would
/// actually touch Azure (<c>TokenCredential.GetTokenAsync</c>), honouring this task's own "no live
/// Azure in unit tests" rule.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAiGatewayModule_resolves_options_with_ADR_004_defaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider();

        // No "AiGateway:Models" configuration section supplied — AiGatewayModelOptions's own
        // property initializers (ADR-004 candidates) must still produce a usable options object.
        var options = provider.GetRequiredService<AiGatewayModelOptions>();
        Assert.Equal("gpt-4o-mini", options.Extract.ModelId);
        Assert.Equal("text-embedding-3-small", options.Embed.ModelId);
        Assert.Equal("prebuilt-read", options.Ocr.ModelId);

        var ocrOptions = provider.GetRequiredService<AiGatewayOcrOptions>();
        Assert.Equal(300, ocrOptions.MaxPagesPerDocument);

        // Task E13/F01/US01/T02: AiGatewayFoundryOptions must also resolve, absent by default —
        // "absent" is exactly what AddAiGatewayModule's IAiGateway factory reads to pick the
        // fixture path.
        var foundryOptions = provider.GetRequiredService<AiGatewayFoundryOptions>();
        Assert.Null(foundryOptions.Endpoint);
        Assert.Null(foundryOptions.OpenAiApiVersion);
        Assert.Equal(40_000, foundryOptions.ClassifyMaxInputChars);

        // The GPT-5.x knobs are absent by default (omitted from the request) and the caps/dimensions
        // carry the ADR-004 amendment defaults.
        Assert.Null(options.Answer.Temperature);
        Assert.Null(options.Extract.ReasoningEffort);
        Assert.Equal(16384, options.Extract.MaxCompletionTokens);
        Assert.Equal(AiGatewayConstants.EmbeddingDimensions, options.Embed.Dimensions);

        var resilience = provider.GetRequiredService<AiGatewayResilienceOptions>();
        Assert.Equal(3, resilience.MaxRetries);
        Assert.NotNull(provider.GetRequiredService<FoundryRetryPolicy>());
    }

    [Fact]
    public void AddAiGatewayModule_binds_the_per_role_knobs_and_the_resilience_section()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiGateway:Models:Answer:Temperature"] = "0.1",
                ["AiGateway:Models:Extract:ReasoningEffort"] = "low",
                ["AiGateway:Models:Extract:MaxCompletionTokens"] = "20000",
                ["AiGateway:Models:Embed:Dimensions"] = "1536",
                ["AiGateway:OpenAiApiVersion"] = "2024-10-21",
                ["AiGateway:Resilience:MaxRetries"] = "1",
                ["AiGateway:Resilience:RequestTimeoutSeconds"] = "30",
                ["AiGateway:Ocr:PollTimeoutSeconds"] = "45",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AiGatewayModelOptions>();
        Assert.Equal(0.1, options.Answer.Temperature);
        Assert.Equal("low", options.Extract.ReasoningEffort);
        Assert.Equal(20000, options.Extract.MaxCompletionTokens);
        Assert.Equal(1536, options.Embed.Dimensions);
        Assert.Equal("2024-10-21", provider.GetRequiredService<AiGatewayFoundryOptions>().OpenAiApiVersion);

        var resilience = provider.GetRequiredService<AiGatewayResilienceOptions>();
        Assert.Equal(1, resilience.MaxRetries);
        Assert.Equal(30, resilience.RequestTimeoutSeconds);
        Assert.Equal(45, provider.GetRequiredService<AiGatewayOcrOptions>().PollTimeoutSeconds);
    }

    [Fact]
    public void AddAiGatewayModule_binds_the_page_budget_from_the_configured_AiGateway_Ocr_section()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiGateway:Ocr:MaxPagesPerDocument"] = "5",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider();
        var ocrOptions = provider.GetRequiredService<AiGatewayOcrOptions>();

        Assert.Equal(5, ocrOptions.MaxPagesPerDocument);
    }

    [Fact]
    public void AddAiGatewayModule_binds_model_ids_from_the_configured_AiGateway_Models_section()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiGateway:Models:Extract:ModelId"] = "custom-extract-model",
                ["AiGateway:Models:Extract:ModelVersion"] = "42",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AiGatewayModelOptions>();

        Assert.Equal("custom-extract-model", options.Extract.ModelId);
        Assert.Equal("42", options.Extract.ModelVersion);

        // Unconfigured roles keep their ADR-004 default — Bind only overlays present keys.
        Assert.Equal("gpt-4o-mini", options.Classify.ModelId);
    }

    [Fact]
    public void AddAiGatewayModule_binds_Endpoint_ProjectName_and_DocumentIntelligenceConnection_from_the_root_AiGateway_section()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiGateway:Endpoint"] = "https://aisvc-contigo.cognitiveservices.azure.com/",
                ["AiGateway:ProjectName"] = "contigo-dev",
                ["AiGateway:DocumentIntelligenceConnection"] = "conn-docint-contigo-dev",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AiGatewayFoundryOptions>();

        Assert.Equal("https://aisvc-contigo.cognitiveservices.azure.com/", options.Endpoint);
        Assert.Equal("contigo-dev", options.ProjectName);
        Assert.Equal("conn-docint-contigo-dev", options.DocumentIntelligenceConnection);

        // Binding the root "AiGateway" section here must not clobber the nested sibling sections
        // AiGatewayModelOptions/AiGatewayOcrOptions bind from their own "AiGateway:Models" /
        // "AiGateway:Ocr" child sections.
        Assert.Equal("gpt-4o-mini", provider.GetRequiredService<AiGatewayModelOptions>().Classify.ModelId);
    }

    [Fact]
    public void AddAiGatewayModule_with_no_endpoint_resolves_LoggingAiGateway_wrapping_FixtureAiGateway()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<IAiGateway>();

        Assert.IsType<LoggingAiGateway>(gateway);
        Assert.IsType<FixtureAiGateway>(GetInnerGateway(gateway));
    }

    [Fact]
    public void AddAiGatewayModule_with_endpoint_set_resolves_LoggingAiGateway_wrapping_FoundryAiGateway()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiGateway:Endpoint"] = "https://aisvc-contigo.cognitiveservices.azure.com/",
                ["AiGateway:ProjectName"] = "contigo-dev",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<IAiGateway>();

        Assert.IsType<LoggingAiGateway>(gateway);
        Assert.IsType<FoundryAiGateway>(GetInnerGateway(gateway));
    }

    [Fact]
    public void AddAiGatewayModule_does_not_override_an_already_registered_IClock()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var preRegisteredClock = new FixedTimeClock();
        services.AddSingleton<IClock>(preRegisteredClock);

        services.AddAiGatewayModule();

        using var provider = services.BuildServiceProvider();

        Assert.Same(preRegisteredClock, provider.GetRequiredService<IClock>());
    }

    /// <summary>Reads <see cref="LoggingAiGateway"/>'s private <c>_inner</c> field — the standard
    /// way to prove a decorator's composition without invoking any behaviour on the wrapped
    /// gateway (see this type's own doc comment for why the Foundry-path test in particular must
    /// never invoke one).</summary>
    private static IAiGateway GetInnerGateway(IAiGateway gateway)
    {
        var field = typeof(LoggingAiGateway).GetField("_inner", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("LoggingAiGateway._inner not found — has it been renamed?");

        return (IAiGateway)field.GetValue(gateway)!;
    }

    private sealed class FixedTimeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Same shape as <c>Contigo.Chat.Tests.ServiceCollectionExtensionsTests.NoOpAuditWriter</c>
    /// — <see cref="Contigo.Audit.Infrastructure.ServiceCollectionExtensions.AddAuditModule"/> is
    /// the module that provides the real one; this module's own tests fake it the same way Chat's
    /// already do, rather than pulling in a real Postgres-backed <c>AuditWriter</c>.</summary>
    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
