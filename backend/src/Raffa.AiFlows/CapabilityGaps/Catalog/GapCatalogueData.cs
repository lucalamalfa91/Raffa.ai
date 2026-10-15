using System.Text.Json;
using System.Text.RegularExpressions;

namespace Raffa.AiFlows.CapabilityGaps.Catalog;

/// <summary>
/// Loads <c>gap-catalogue.json</c> (INV-07): a <c>version</c>, a changelog and the ordered entries
/// (order is match priority, first match wins). Each entry carries its it/en texts, its
/// <see cref="GapAlternative"/> and the <c>include</c>/<c>exclude</c> regexes, compiled
/// <c>IgnoreCase | CultureInvariant | Compiled</c> exactly as the code-defined catalog was. A
/// malformed or missing file throws on first use: the catalogue is not optional (the domain gate
/// reads it on every turn), so a broken deploy must fail loudly, not silently match nothing.
/// </summary>
internal sealed class GapCatalogueData
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private GapCatalogueData(string version, IReadOnlyList<CapabilityGap> entries)
    {
        Version = version;
        Entries = entries;
    }

    public string Version { get; }

    public IReadOnlyList<CapabilityGap> Entries { get; }

    public static GapCatalogueData LoadEmbedded()
    {
        using var stream = typeof(GapCatalogueData).Assembly.GetManifestResourceStream(CapabilityGapCatalog.ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {CapabilityGapCatalog.ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static GapCatalogueData Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var version = root.GetProperty("version").GetString() ?? throw new InvalidDataException("The gap catalogue has no version.");

        var entries = new List<CapabilityGap>();
        foreach (var node in root.GetProperty("entries").EnumerateArray())
        {
            string Text(string name) => node.GetProperty(name).GetString() ?? throw new InvalidDataException($"Gap entry field '{name}' is null.");

            var key = Text("key");
            if (entries.Any(e => e.Key == key))
            {
                throw new InvalidDataException($"Duplicate gap key '{key}'.");
            }

            var alternative = Enum.Parse<GapAlternative>(Text("alternative"));
            if (alternative == GapAlternative.NearestCapability)
            {
                throw new InvalidDataException($"Gap '{key}': NearestCapability is for discovered gaps only.");
            }

            var exclude = node.TryGetProperty("exclude", out var ex) && ex.ValueKind == JsonValueKind.String
                ? new Regex(ex.GetString()!, Options)
                : null;

            entries.Add(new CapabilityGap(
                key,
                Text("titleEn"),
                Text("titleIt"),
                Text("operationEn"),
                Text("operationIt"),
                Text("alternativeEn"),
                Text("alternativeIt"),
                Text("descriptionEn"),
                Text("descriptionIt"),
                alternative,
                new Regex(Text("include"), Options),
                exclude));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("The gap catalogue has no entries.");
        }

        return new GapCatalogueData(version, entries);
    }
}
