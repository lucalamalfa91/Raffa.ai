using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>Configuration section <c>RenewalReviews</c>: the per-client-IP fixed window of
/// <c>POST /api/public/renewal-reviews</c> (default 5 requests per 10 minutes).</summary>
internal sealed class RenewalReviewOptions
{
    public const string SectionName = "RenewalReviews";

    public int PermitLimit { get; set; } = 5;

    public int WindowMinutes { get; set; } = 10;
}

/// <summary>
/// The marketing landing page's "Free renewal review" lead form. <c>POST /api/public/renewal-reviews</c> is anonymous
/// (no identity, no <c>X-Tenant-Id</c>): it validates, drops honeypot submissions silently and stores the lead in the
/// platform-level <c>renewal_review_request</c> table (<see cref="RenewalReviewStore"/>), never the IP or user agent.
/// It is the only route behind the <see cref="RateLimitPolicy"/> limiter. <c>GET /api/admin/renewal-reviews</c> lists the
/// leads for the Super Admin, guarded as the rest of <c>/api/admin/*</c>: 401 anonymous, 404 for anyone who is not a
/// platform admin.
/// </summary>
public static partial class RenewalReviewEndpointExtensions
{
    public const string RateLimitPolicy = "public-renewal-review";

    /// <summary>The exact choices of the landing form (with the en dash, U+2013, in "1–3 months" and "3–6 months").</summary>
    public static readonly IReadOnlyList<string> RenewalChoices =
        ["Within 30 days", "1–3 months", "3–6 months", "More than 6 months", "Not sure yet"];

    private const int MaxBodyBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registers the rate limiter with one policy, applied only to the public submit route. Partitioned by
    /// <see cref="ClientAddress"/>: the first <c>X-Forwarded-For</c> entry the ingress adds, else the connection address.</summary>
    public static IServiceCollection AddRenewalReviewRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RenewalReviewOptions.SectionName).Get<RenewalReviewOptions>() ?? new RenewalReviewOptions();
        var permitLimit = Math.Max(1, options.PermitLimit);
        var window = TimeSpan.FromMinutes(Math.Max(1, options.WindowMinutes));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many requests. Please try again later." }, ct).ConfigureAwait(false);
            };
            limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });
        return services;
    }

    public static IEndpointRouteBuilder MapRenewalReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/public/renewal-reviews", SubmitAsync).RequireRateLimiting(RateLimitPolicy);
        endpoints.MapGet("/api/admin/renewal-reviews", (ICallerIdentity id, PlatformAdminGuard guard, RenewalReviewStore store, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => ListAsync(store, ct)));
        return endpoints;
    }

    private static async Task<IResult> SubmitAsync(HttpRequest request, RenewalReviewStore store, IClock clock, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBodyBytes)
        {
            return Error("Invalid request.");
        }

        RenewalReviewSubmission? body;
        try
        {
            // Read regardless of Content-Type, so a simple (preflight-free) cross-origin post works too.
            body = await JsonSerializer.DeserializeAsync<RenewalReviewSubmission>(request.Body, Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Error("Invalid request.");
        }

        if (body is null)
        {
            return Error("Invalid request.");
        }

        // Honeypot: a bot filled the hidden field. Same answer as a real success, nothing stored.
        if (!string.IsNullOrWhiteSpace(body.Website))
        {
            return Ok();
        }

        var name = body.Name?.Trim() ?? string.Empty;
        var email = body.Email?.Trim() ?? string.Empty;
        var company = body.Company?.Trim() ?? string.Empty;
        var renewal = body.Renewal?.Trim() ?? string.Empty;

        if (name.Length is 0 or > 120)
        {
            return Error("Please enter your name (up to 120 characters).");
        }

        if (email.Length > 254 || !EmailShape().IsMatch(email))
        {
            return Error("Please enter a valid work email.");
        }

        if (company.Length is 0 or > 160)
        {
            return Error("Please enter your company name (up to 160 characters).");
        }

        if (!RenewalChoices.Contains(renewal, StringComparer.Ordinal))
        {
            return Error("Please choose when your next renewal is due.");
        }

        if (body.Consent is not true)
        {
            return Error("Please agree to be contacted about your renewal review.");
        }

        await store.SaveAsync(
            new RenewalReviewRequestRow
            {
                Id = Guid.NewGuid(),
                Name = name,
                Email = email,
                Company = company,
                RenewalTiming = renewal,
                Consent = true,
                CreatedAt = clock.UtcNow.ToUniversalTime(),
                Source = RenewalReviewStore.LandingSource,
            },
            ct).ConfigureAwait(false);
        return Ok();
    }

    private static async Task<IResult> ListAsync(RenewalReviewStore store, CancellationToken ct)
    {
        var rows = await store.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            items = rows.Select(r => new
            {
                id = r.Id, name = r.Name, email = r.Email, company = r.Company, renewal = r.RenewalTiming, createdAt = r.CreatedAt,
            }),
        });
    }

    private static IResult Ok() => Results.Json(new { ok = true }, statusCode: StatusCodes.Status201Created);

    private static IResult Error(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Basic shape only: one <c>@</c>, no whitespace, a dot in the domain.</summary>
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s.]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShape();

    /// <summary>
    /// The visitor's address for the rate limit only. Behind the Container Apps ingress every
    /// connection comes from the proxy, which puts the real client first in <c>X-Forwarded-For</c>;
    /// without that header (local runs, tests) the connection's own address is used. The header is
    /// read here and nowhere else, so a forged value can at worst give its sender a bucket of its own
    /// on this one public form, never change who the app thinks the caller is.
    /// </summary>
    internal static string ClientAddress(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (first.Length is > 0 and <= 64)
            {
                return first;
            }
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

/// <summary>The landing form's JSON body. Every field is optional here so a missing one reads as a 400 from
/// validation, not a deserialization failure; <c>Website</c> is the honeypot.</summary>
public sealed record RenewalReviewSubmission
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Company { get; init; }
    public string? Renewal { get; init; }
    public bool? Consent { get; init; }
    public string? Website { get; init; }
}
