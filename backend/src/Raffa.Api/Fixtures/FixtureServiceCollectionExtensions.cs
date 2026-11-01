using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.Chat.Infrastructure.Fixtures;
using Raffa.Documents.Contracts.Infrastructure.Fixtures;
using Raffa.Quotes.Infrastructure.Fixtures;
using Raffa.Identity.Workspace.Infrastructure.Fixtures;
using Raffa.SharedKernel;

namespace Raffa.Api.Fixtures;

/// <summary>Host wiring of fixture mode (IMPLEMENTATION-PLAN.md §2). Every call is a no-op when
/// <c>Fixtures:Enabled</c> is false.</summary>
internal static class FixtureServiceCollectionExtensions
{
    public const string CorsPolicyName = "fixtures";

    /// <summary>Binds and validates <c>Fixtures:*</c>; must run before any <c>AddXxxModule</c> so the
    /// scenario clock wins over the modules' <c>TryAdd</c> system clock.</summary>
    public static FixtureOptions AddFixtureMode(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(FixtureOptions.SectionName).Get<FixtureOptions>() ?? new FixtureOptions();
        options.ValidateOrThrow(builder.Environment.EnvironmentName);
        builder.Services.AddSingleton(options);
        if (!options.Enabled)
        {
            return options;
        }

        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<IClock, FixtureClock>();
        builder.Services.AddSingleton<FixtureSessionState>();
        builder.Services.AddScoped<FixtureResetService>();

        // One seeder per module that owns fixture data; later slices add their line here.
        builder.Services.AddIdentityWorkspaceFixtureSeeder();
        builder.Services.AddDocumentsContractsFixtureSeeder();
        builder.Services.AddQuotesFixtureSeeder();
        builder.Services.AddChatFixtureSeeder();

        if (options.CorsOrigins.Length > 0)
        {
            builder.Services.AddCors(cors => cors.AddPolicy(CorsPolicyName, policy => policy
                .WithOrigins(options.CorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()));
        }

        return options;
    }

    public static AuthenticationBuilder AddFixtureScheme(this AuthenticationBuilder authentication, FixtureOptions options)
    {
        if (options.Enabled)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, FixtureAuthenticationHandler>(
                FixtureAuthenticationHandler.SchemeName, configureOptions: null);
        }

        return authentication;
    }

    public static WebApplication UseFixtureMode(this WebApplication app, FixtureOptions options)
    {
        if (options.Enabled && options.CorsOrigins.Length > 0)
        {
            app.UseCors(CorsPolicyName);
        }

        return app;
    }

    public static WebApplication MapFixtureMode(this WebApplication app, FixtureOptions options)
    {
        if (options.Enabled)
        {
            app.MapFixtureEndpoints();
        }

        return app;
    }
}
