using System.Globalization;
using Npgsql;
using Raffa.SharedKernel;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Turns "Postgres could not hand this request a connection" into an honest <c>503 Service
/// Unavailable</c> with <c>Retry-After</c>, instead of the bare 500 an unhandled exception produces.
///
/// <para>
/// The shapes it recognises are the ones a saturated <c>Standard_B1ms</c> server (50 connections,
/// shared by every replica of both hosts) actually produces: the server refusing the connection
/// outright (<c>PostgresException</c> <c>53300 too_many_connections</c> / <c>53400
/// configuration_limit_exceeded</c>), Npgsql's own bounded pool timing out while this process
/// waits its turn (<c>NpgsqlException</c> "The connection pool has been exhausted" wrapping a
/// <see cref="TimeoutException"/> -- see <c>Raffa.SharedKernel.Persistence.PostgresConnectionPool</c>),
/// and the EF Core retry-exhaustion form <see cref="TransientDataAccessFault"/> already names.
/// Every one of them is transient by definition -- the same request succeeds a moment later --
/// which is exactly what 503 + <c>Retry-After</c> says and what the web already renders as
/// "temporarily unavailable. Try again in a moment." on Renewals, Contract 360 and Portfolio.
/// </para>
///
/// <para>
/// Deliberately a narrow <c>try/catch</c> middleware rather than <c>UseExceptionHandler</c> +
/// <c>AddProblemDetails</c>: anything that is not database saturation is rethrown untouched, so a
/// real bug still surfaces exactly as it did before (the developer exception page in Development,
/// a bare 500 in production, the same failure text the API test helpers already read).
/// </para>
/// </summary>
internal static class DatabaseSaturationExceptionHandler
{
    /// <summary>Seconds a client should wait before retrying -- one poll cadence (2 s) is enough for a pool to free up.</summary>
    public const int RetryAfterSeconds = 2;

    public const string ErrorMessage = "Raffa.ai's database is busy right now. Try again in a moment.";

    private const string TooManyConnections = "53300";
    private const string ConfigurationLimitExceeded = "53400";

    public static bool IsDatabaseSaturation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException { SqlState: TooManyConnections or ConfigurationLimitExceeded }:
                    return true;
                case NpgsqlException
                    {
                        InnerException: TimeoutException,
                    } npgsql when npgsql.Message.Contains("pool has been exhausted", StringComparison.OrdinalIgnoreCase):
                    return true;
            }

            if (current.Message.Contains(TransientDataAccessFault.EfRetryExhaustedMessage, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Answers a database-saturation exception with 503 + <c>Retry-After</c> + a one-line JSON
    /// error, provided the response has not started; every other exception propagates.
    /// </summary>
    public static IApplicationBuilder UseDatabaseSaturationHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (httpContext, next) =>
        {
            try
            {
                await next(httpContext).ConfigureAwait(false);
            }
            catch (Exception exception) when (!httpContext.Response.HasStarted && IsDatabaseSaturation(exception))
            {
                await WriteAsync(httpContext, exception).ConfigureAwait(false);
            }
        });
    }

    public static async Task WriteAsync(HttpContext httpContext, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseSaturationExceptionHandler))
            .LogWarning(
                exception,
                "Postgres could not serve a connection for {Method} {Path}; answered 503 with Retry-After {RetryAfter}s.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                RetryAfterSeconds);

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await httpContext.Response
            .WriteAsJsonAsync(new { error = ErrorMessage }, httpContext.RequestAborted)
            .ConfigureAwait(false);
    }
}
