namespace Raffa.Api.Infrastructure;

/// <summary>Configuration section <c>FxRates</c>: the daily download of the ECB reference rates.</summary>
internal sealed class FxReferenceRateOptions
{
    public const string SectionName = "FxRates";

    /// <summary>Off: no download (the stored rates, if any, are still used).</summary>
    public bool Enabled { get; set; } = true;

    public string EcbDailyUrl { get; set; } = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    /// <summary>The local hour (in <see cref="TimeZone"/>) from which each morning's download runs.</summary>
    public int RefreshHour { get; set; } = 6;

    public string TimeZone { get; set; } = "Europe/Zurich";

    /// <summary>How often the loop wakes to check whether today's download is still due (and to retry a failure).</summary>
    public int CheckIntervalMinutes { get; set; } = 30;
}
