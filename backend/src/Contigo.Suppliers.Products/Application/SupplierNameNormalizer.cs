using System.Text;

namespace Contigo.Suppliers.Products.Application;

/// <summary>
/// Turns a raw, as-extracted or as-typed supplier name into the canonical key
/// <see cref="SupplierResolver"/> matches on (parent story us-01-supplier-identity AC-2):
/// lower-case, legal-suffix-stripped, punctuation-stripped, whitespace-collapsed. Deterministic
/// and pure — no database, no culture-dependent casing (uses ordinal/invariant comparisons
/// throughout so the same input always normalizes the same way regardless of the host's locale).
/// </summary>
public static class SupplierNameNormalizer
{
    /// <summary>
    /// Every legal-entity suffix `inputs/requirements.md` R-SUP-02 names, reduced to the single
    /// token it collapses to once periods are stripped and casing is lowered: "SA"/"S.A." both
    /// become "sa", "SpA"/"S.p.A." both become "spa", "S.r.l."/"Srl" both become "srl", "Inc"/
    /// "Inc." both become "inc", "Co." becomes "co" — so a dotted and an undotted spelling of the
    /// same suffix are treated identically without needing two separate entries here.
    /// </summary>
    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal)
    {
        "inc", "ltd", "limited", "gmbh", "ag", "sa", "spa", "srl", "llc", "corp", "corporation", "co",
    };

    /// <summary>
    /// Normalizes <paramref name="rawName"/>. Throws <see cref="ArgumentException"/> for a
    /// null/blank input — a supplier name empty enough to normalize to nothing is a caller bug
    /// (an extracted "supplier" fact or a typed name should never be blank), not a value this
    /// method silently accepts.
    /// </summary>
    public static string Normalize(string rawName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawName);

        var lowered = rawName.Trim().ToLowerInvariant();

        // Punctuation is dropped outright rather than replaced with a space, so a dotted
        // abbreviation like "s.p.a." collapses onto the exact same token as the undotted "spa"
        // (both become "spa") instead of fragmenting into single letters ("s", "p", "a"). Existing
        // whitespace is preserved (as a single space) so word boundaries survive.
        var builder = new StringBuilder(lowered.Length);
        foreach (var c in lowered)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                builder.Append(' ');
            }
            // every other character (periods, commas, apostrophes, hyphens, ...) is dropped.
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Strip trailing legal-suffix words one at a time (a name could carry more than one, e.g.
        // "Foo Ltd Corp" after a prior merger) — but never down to zero words, so a supplier
        // literally named after a bare suffix token still normalizes to something non-empty.
        var end = words.Length;
        while (end > 1 && LegalSuffixes.Contains(words[end - 1]))
        {
            end--;
        }

        return string.Join(' ', words, 0, end);
    }
}
