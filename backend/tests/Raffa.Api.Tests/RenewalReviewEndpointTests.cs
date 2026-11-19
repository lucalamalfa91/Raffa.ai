using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.Api.Tests.TestSupport;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests;

/// <summary>
/// The marketing landing page's "Free renewal review" form on the in-memory host (where
/// <c>RenewalReviewStore</c> keeps a process-local copy): <c>POST /api/public/renewal-reviews</c> is anonymous, validates,
/// silently drops honeypot submissions and is rate limited per client; <c>GET /api/admin/renewal-reviews</c> lists the
/// leads newest first for the platform admin only (401 anonymous, 404 everyone else). The Postgres path of the store is
/// covered in <see cref="PlatformAdminEndpointTests"/>.
/// </summary>
public sealed class RenewalReviewEndpointTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string Staff = "staff@raffa.example";
    private const string Customer = "someone@customer.example";
    private const string SubmitPath = "/api/public/renewal-reviews";
    private const string ListPath = "/api/admin/renewal-reviews";

    private readonly SteppingClock _clock = new(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero));

    private WebApplicationFactory<Program> Host(int permitLimit = 1000) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PlatformAdmin:ObjectIds:0", Staff);
            builder.UseSetting("RenewalReviews:PermitLimit", permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            });
        });

    private static object Valid(string company, string? website = null, string renewal = "1–3 months") => new
    {
        name = "  Giulia Rossi  ",
        email = " giulia.rossi@acme.example ",
        company,
        renewal,
        consent = true,
        website,
    };

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("203.0.113.7, 10.0.0.4", "203.0.113.7")]
    [InlineData("", "10.1.2.3")]
    public void Rate_limit_key_is_the_first_forwarded_client_else_the_connection_address(string forwarded, string expected)
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        if (forwarded.Length > 0)
        {
            http.Request.Headers["X-Forwarded-For"] = forwarded;
        }

        Assert.Equal(expected, RenewalReviewEndpointExtensions.ClientAddress(http));
    }

    [Fact]
    public async Task An_anonymous_visitor_submits_a_lead_and_the_platform_admin_sees_it()
    {
        var client = Host().CreateClient();
        var company = $"Acme {Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(SubmitPath, Valid($"  {company}  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
        }

        var item = (await ListAsync(client)).Single(i => i.GetProperty("company").GetString() == company);
        Assert.Equal("Giulia Rossi", item.GetProperty("name").GetString());
        Assert.Equal("giulia.rossi@acme.example", item.GetProperty("email").GetString());
        Assert.Equal("1–3 months", item.GetProperty("renewal").GetString());
        Assert.NotEqual(Guid.Empty, item.GetProperty("id").GetGuid());
        Assert.Equal(_clock.UtcNow, item.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("", "a@b.example", "Acme", "Not sure yet", true, "Please enter your name (up to 120 characters).")]
    [InlineData("   ", "a@b.example", "Acme", "Not sure yet", true, "Please enter your name (up to 120 characters).")]
    [InlineData("Giulia", "not-an-email", "Acme", "Not sure yet", true, "Please enter a valid work email.")]
    [InlineData("Giulia", "a b@c.example", "Acme", "Not sure yet", true, "Please enter a valid work email.")]
    [InlineData("Giulia", "a@nodot", "Acme", "Not sure yet", true, "Please enter a valid work email.")]
    [InlineData("Giulia", "a@b.example", "", "Not sure yet", true, "Please enter your company name (up to 160 characters).")]
    [InlineData("Giulia", "a@b.example", "Acme", "1-3 months", true, "Please choose when your next renewal is due.")]
    [InlineData("Giulia", "a@b.example", "Acme", "Next year", true, "Please choose when your next renewal is due.")]
    [InlineData("Giulia", "a@b.example", "Acme", "Within 30 days", false, "Please agree to be contacted about your renewal review.")]
    public async Task An_invalid_submission_is_a_400_with_a_short_message(
        string name, string email, string company, string renewal, bool consent, string expected)
    {
        var client = Host().CreateClient();

        var response = await client.PostAsJsonAsync(SubmitPath, new { name, email, company, renewal, consent, website = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await ErrorAsync(response));
    }

    [Fact]
    public async Task Lengths_are_checked_after_trimming()
    {
        var client = Host().CreateClient();

        async Task<HttpStatusCode> Post(string name, string email, string company) =>
            (await client.PostAsJsonAsync(SubmitPath, new { name, email, company, renewal = "More than 6 months", consent = true })).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Post(new string('n', 121), "a@b.example", "Acme"));
        Assert.Equal(HttpStatusCode.BadRequest, await Post("Giulia", new string('e', 250) + "@b.example", "Acme"));
        Assert.Equal(HttpStatusCode.BadRequest, await Post("Giulia", "a@b.example", new string('c', 161)));
        Assert.Equal(HttpStatusCode.Created, await Post($"  {new string('n', 120)}  ", "a@b.example", $"{new string('c', 128)}{Guid.NewGuid():N}"));
    }

    [Fact]
    public async Task A_missing_consent_or_a_malformed_body_is_a_400()
    {
        var client = Host().CreateClient();

        var noConsent = await client.PostAsJsonAsync(SubmitPath, new { name = "Giulia", email = "a@b.example", company = "Acme", renewal = "Not sure yet" });
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Equal("Please agree to be contacted about your renewal review.", await ErrorAsync(noConsent));

        var malformed = await client.PostAsync(SubmitPath, new StringContent("{not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("Invalid request.", await ErrorAsync(malformed));
    }

    [Fact]
    public async Task A_filled_honeypot_answers_success_and_stores_nothing()
    {
        var client = Host().CreateClient();
        var company = $"Bot Corp {Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(SubmitPath, Valid(company, website: "https://spam.example"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
        }

        Assert.DoesNotContain(await ListAsync(client), i => i.GetProperty("company").GetString() == company);
    }

    [Fact]
    public async Task Only_a_platform_admin_lists_the_leads()
    {
        var client = Host().CreateClient();

        // Anonymous: 401 on a real host; this in-memory test host presents a default caller, so 404 here. Never 200.
        Assert.Contains((await client.GetAsync(ListPath)).StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound });
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, ListPath, Customer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, ListPath, Staff)).StatusCode);
    }

    [Fact]
    public async Task The_list_is_newest_first()
    {
        var client = Host().CreateClient();
        var older = $"Older {Guid.NewGuid():N}";
        var newer = $"Newer {Guid.NewGuid():N}";

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(SubmitPath, Valid(older))).StatusCode);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(SubmitPath, Valid(newer))).StatusCode);

        var companies = (await ListAsync(client)).Select(i => i.GetProperty("company").GetString()).ToList();
        Assert.True(companies.IndexOf(newer) >= 0 && companies.IndexOf(newer) < companies.IndexOf(older));
        Assert.True(companies.Count <= 500);
    }

    [Fact]
    public async Task A_client_over_the_window_gets_a_429()
    {
        var client = Host(permitLimit: 2).CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(SubmitPath, Valid($"Rate {Guid.NewGuid():N}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(SubmitPath, new { name = "" })).StatusCode);

        var limited = await client.PostAsJsonAsync(SubmitPath, Valid($"Rate {Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Too many requests. Please try again later.", await ErrorAsync(limited));

        // The limiter is scoped to the public form: the admin list is not limited.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, ListPath, Staff)).StatusCode);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString();
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-User-Id", user);
        return client.SendAsync(request);
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client)
    {
        var response = await GetAsync(client, ListPath, Staff);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone())];
    }

    private sealed class SteppingClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
