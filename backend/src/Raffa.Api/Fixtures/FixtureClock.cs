using System.Globalization;
using Raffa.SharedKernel;

namespace Raffa.Api.Fixtures;

/// <summary>
/// The scenario clock of fixture mode: the date of the request's <c>X-Raffa-Now</c> header
/// (<c>YYYY-MM-DD</c>), else <c>Fixtures:Now</c> (default 2026-09-08), at the real UTC time of day
/// (so timestamps written in one session keep their order). Registered as <see cref="IClock"/>
/// before every module (they all <c>TryAdd</c> theirs) only when fixtures are enabled.
/// </summary>
internal sealed class FixtureClock(IHttpContextAccessor httpContextAccessor, FixtureOptions options) : IClock
{
    public const string HeaderName = "X-Raffa-Now";

    public DateTimeOffset UtcNow
    {
        get
        {
            var real = DateTimeOffset.UtcNow;
            var date = Today(httpContextAccessor.HttpContext?.Request, options);
            return new DateTimeOffset(date.ToDateTime(TimeOnly.FromTimeSpan(real.TimeOfDay)), TimeSpan.Zero);
        }
    }

    public static DateOnly Today(HttpRequest? request, FixtureOptions options)
    {
        if (request is not null
            && request.Headers.TryGetValue(HeaderName, out var values)
            && DateOnly.TryParseExact(values.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return options.Now;
    }
}
