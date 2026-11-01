using System.Text.Json;
using System.Text.RegularExpressions;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// Reads a generated Italian catalogue (<c>web/src/i18n/it/case4.ts</c>) from the repository so the
/// tests prove every engine message id has its Italian text, equal to the prototype's.
/// </summary>
internal static partial class ItalianCatalogue
{
    public static readonly Lazy<Dictionary<string, string>> Case4 = new(() => Load("case4.ts"));

    public static Dictionary<string, string> Load(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "web", "src", "i18n", "it", file)))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new FileNotFoundException("web/src/i18n/it/" + file + " not found above " + AppContext.BaseDirectory);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(dir.FullName, "web", "src", "i18n", "it", file)))
        {
            var m = Entry().Match(line);
            if (m.Success)
            {
                map[JsonSerializer.Deserialize<string>(m.Groups[1].Value)!] = JsonSerializer.Deserialize<string>(m.Groups[2].Value)!;
            }
        }

        return map;
    }

    [GeneratedRegex(@"^\s*(""(?:[^""\\]|\\.)*""):\s*(""(?:[^""\\]|\\.)*""),\s*$")]
    private static partial Regex Entry();
}
