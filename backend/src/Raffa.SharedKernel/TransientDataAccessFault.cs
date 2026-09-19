namespace Raffa.SharedKernel;

/// <summary>
/// Detects the EF Core / Npgsql retry-exhaustion shape that live classify outages were
/// mis-attributed to "the classify role could not be reached". The exact
/// <see cref="InvalidOperationException"/> text is CoreStrings.TransientExceptionDetected —
/// a database retry storm, not Azure OpenAI.
/// </summary>
public static class TransientDataAccessFault
{
    /// <summary>EF Core <c>ExecutionStrategy</c> exhaustion (CoreStrings.TransientExceptionDetected).</summary>
    public const string EfRetryExhaustedMessage =
        "An exception has been raised that is likely due to a transient failure.";

    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }

            if (current.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("too_many_connections", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("53300", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
