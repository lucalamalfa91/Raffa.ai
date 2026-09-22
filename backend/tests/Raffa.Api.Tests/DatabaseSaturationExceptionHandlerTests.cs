using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Raffa.Api.Infrastructure;

namespace Raffa.Api.Tests;

/// <summary>
/// <see cref="DatabaseSaturationExceptionHandler"/> is the API's one answer to a Postgres that has
/// no connection left to give (demo, 2026-09-22: a 50-connection <c>Standard_B1ms</c> server behind
/// every replica of both hosts). These pin which exceptions count as saturation -- the server's own
/// refusal, Npgsql's bounded-pool timeout, EF's retry exhaustion -- and that the answer is a 503
/// with <c>Retry-After</c> and a one-line error, while any other exception stays a real failure.
/// </summary>
public sealed class DatabaseSaturationExceptionHandlerTests
{
    [Fact]
    public void The_servers_own_too_many_connections_refusal_is_saturation()
    {
        var refused = new PostgresException("sorry, too many clients already", "FATAL", "FATAL", "53300");

        Assert.True(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(refused));
        Assert.True(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(new InvalidOperationException("wrapped", refused)));
    }

    [Fact]
    public void A_configuration_limit_refusal_is_saturation()
    {
        var refused = new PostgresException("remaining connection slots are reserved", "FATAL", "FATAL", "53400");

        Assert.True(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(refused));
    }

    [Fact]
    public void Npgsqls_exhausted_pool_timeout_is_saturation()
    {
        var exhausted = new NpgsqlException(
            "The connection pool has been exhausted, either raise 'Max Pool Size' (currently 4) or 'Timeout' (currently 15 seconds) in your connection string.",
            new TimeoutException());

        Assert.True(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(exhausted));
    }

    [Fact]
    public void An_unrelated_npgsql_timeout_is_not_saturation()
    {
        var timedOut = new NpgsqlException("Timed out while executing the command.", new TimeoutException());

        Assert.False(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(timedOut));
    }

    [Fact]
    public void Ef_retry_exhaustion_is_saturation()
    {
        var exhausted = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new PostgresException("could not serialize access", "ERROR", "ERROR", "40001"));

        Assert.True(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(exhausted));
    }

    [Theory]
    [InlineData("42P01")] // undefined_table: a real bug, never "try again"
    [InlineData("23505")] // unique_violation
    public void Any_other_postgres_error_is_not_saturation(string sqlState)
    {
        var failure = new PostgresException("boom", "ERROR", "ERROR", sqlState);

        Assert.False(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(failure));
    }

    [Fact]
    public void An_ordinary_exception_is_not_saturation()
    {
        Assert.False(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(new InvalidOperationException("Sequence contains no elements")));
        Assert.False(DatabaseSaturationExceptionHandler.IsDatabaseSaturation(new ArgumentOutOfRangeException("risk")));
    }

    [Fact]
    public async Task WriteAsync_answers_503_with_retry_after_and_a_one_line_error()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/renewals";
        httpContext.Response.Body = new MemoryStream();

        await DatabaseSaturationExceptionHandler.WriteAsync(
            httpContext, new PostgresException("sorry, too many clients already", "FATAL", "FATAL", "53300"));

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, httpContext.Response.StatusCode);
        Assert.Equal(DatabaseSaturationExceptionHandler.RetryAfterSeconds.ToString(), httpContext.Response.Headers.RetryAfter.ToString());
        Assert.StartsWith("application/json", httpContext.Response.ContentType);

        httpContext.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(DatabaseSaturationExceptionHandler.ErrorMessage, body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_middleware_answers_saturation_and_rethrows_everything_else()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        var saturated = await RunAsync(services, static _ => throw new PostgresException("sorry, too many clients already", "FATAL", "FATAL", "53300"));
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, saturated.Response.StatusCode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(services, static _ => throw new InvalidOperationException("a real bug")));
    }

    private static async Task<HttpContext> RunAsync(IServiceProvider services, RequestDelegate terminal)
    {
        var app = new ApplicationBuilder(services);
        app.UseDatabaseSaturationHandling();
        app.Run(terminal);
        var pipeline = app.Build();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();
        await pipeline(httpContext);
        return httpContext;
    }
}
