using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;

namespace Raffa.Api.Fixtures;

/// <summary>Request body of <c>POST /api/fixtures/reset</c>.</summary>
internal sealed record FixtureResetRequest(string? Scenario);

internal sealed record FixtureUserBody(string Key, string DisplayName, string Title, string Email, string Role, bool IsApprover);

/// <summary>Body of <c>GET /api/fixtures/session</c> and of a successful reset.</summary>
internal sealed record FixtureSessionBody(
    Guid TenantId, string TenantName, string? Scenario, DateOnly Now, IReadOnlyList<FixtureUserBody> Users);

/// <summary>
/// The two fixture-mode endpoints, mapped only when <c>Fixtures:Enabled</c> is true (a Production
/// host refuses to start with it on). No caller identity: the reset is what creates the members.
/// </summary>
internal static class FixturesEndpointExtensions
{
    public static IEndpointRouteBuilder MapFixtureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/fixtures/reset", ResetAsync);
        endpoints.MapGet("/api/fixtures/session", GetSession);
        return endpoints;
    }

    private static async Task<IResult> ResetAsync(
        FixtureResetRequest? request, HttpRequest httpRequest, FixtureResetService resetService,
        FixtureSessionState state, FixtureOptions options, CancellationToken cancellationToken)
    {
        var scenario = request?.Scenario?.Trim().ToLowerInvariant();
        if (!FixtureScenarios.IsKnown(scenario))
        {
            return Results.BadRequest(new { error = "scenario must be \"zero\" or \"full\"." });
        }

        await resetService.ResetAsync(scenario!, cancellationToken).ConfigureAwait(false);
        return Results.Ok(Session(httpRequest, state, options));
    }

    private static IResult GetSession(HttpRequest httpRequest, FixtureSessionState state, FixtureOptions options) =>
        Results.Ok(Session(httpRequest, state, options));

    private static FixtureSessionBody Session(HttpRequest request, FixtureSessionState state, FixtureOptions options) =>
        new(FixtureTenant.TenantId.Value, FixtureTenant.Name, state.Scenario, FixtureClock.Today(request, options),
            FixtureTenant.Users.Select(u => new FixtureUserBody(u.Key, u.DisplayName, u.Title, u.Email, u.Role, u.IsApprover)).ToList());
}
