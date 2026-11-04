namespace Raffa.SharedKernel.Contracts;

/// <summary>
/// Whether a contract line recurs. Extraction records each line's billing period as a short English word
/// (monthly, quarterly, annual, one-time); a one-off amount (onboarding, setup) is paid once and is not
/// part of what a renewal renews. A line whose period was not read counts as recurring: the renewal scope
/// keeps it rather than dropping a cost nobody checked.
/// </summary>
public static class ContractBillingPeriod
{
    private static readonly HashSet<string> OneOffWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "one-time", "one time", "onetime", "one-off", "one off", "once", "non-recurring", "nonrecurring",
        "una tantum", "einmalig",
    };

    public static bool IsOneOff(string? billingPeriod) =>
        !string.IsNullOrWhiteSpace(billingPeriod) && OneOffWords.Contains(billingPeriod.Trim());
}
