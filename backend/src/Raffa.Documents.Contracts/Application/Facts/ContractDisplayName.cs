namespace Raffa.Documents.Contracts.Application.Facts;

/// <summary>
/// The readable commercial name of a real contract: its supplier and its document type ("Veeam Order
/// Form", "GitLab MSA"; "Veeam" when the type says nothing). It replaces the uploaded file name, which
/// leaked into the case emails' subject and text ("Veeam_Subscription_2024.pdf — renewal discussion").
/// Only what the contract states: no product or term is guessed from the file name.
/// </summary>
public static class ContractDisplayName
{
    /// <summary>The same labels the Portfolio shows for <see cref="Domain.ContractDocumentType"/>.</summary>
    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Msa"] = "MSA",
        ["OrderForm"] = "Order Form",
        ["Amendment"] = "Amendment",
        ["Sow"] = "SOW",
        ["RenewalLetter"] = "Renewal Letter",
        ["Quote"] = "Quote",
        ["Invoice"] = "Invoice",
        ["PriceList"] = "Price List",
        ["Nda"] = "NDA",
        ["Dpa"] = "DPA",
    };

    public static string Of(string? supplier, string? type)
    {
        var who = supplier?.Trim() ?? string.Empty;
        var label = type is not null && TypeLabels.TryGetValue(type.Trim(), out var known) ? known : null;
        return (who.Length > 0, label) switch
        {
            (true, { } l) => who + " " + l,
            (true, null) => who,
            (false, { } l) => l,
            _ => "Contract",
        };
    }

    /// <summary>
    /// Facts written before this name existed carry the file name as their name. A real contract's
    /// name equal to its file name is read as the display name instead, so no stored row keeps
    /// leaking the file into an email. Fixture contracts keep their curated names.
    /// </summary>
    public static Raffa.SharedKernel.Contracts.ContractFacts Normalize(Raffa.SharedKernel.Contracts.ContractFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.FixtureId is null && facts.File.Length > 0 && string.Equals(facts.Name, facts.File, StringComparison.Ordinal)
            ? facts with { Name = Of(facts.Supplier, facts.Type) }
            : facts;
    }
}
