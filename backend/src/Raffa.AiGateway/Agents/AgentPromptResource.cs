using System.Reflection;

namespace Raffa.AiGateway.Agents;

/// <summary>
/// Reads an agent's prompt from the versioned file it lives in (plan 3.2 and T-06: prompts are
/// files, <c>Prompts/&lt;flow&gt;/&lt;agent&gt;/vN.md</c>, reviewed and diffed as files). The owning
/// assembly embeds the folder (<c>&lt;EmbeddedResource Include="Prompts\**\*.md" /&gt;</c>), so the
/// text that ships is the text that was reviewed, with no path to resolve at run time; the hash
/// registered beside the version in code (<see cref="AgentDefinition.RegisteredPromptHash"/>) is what
/// turns an edit without a version bump into a failing test.
///
/// The whole file is the prompt: no front matter, no wrapper. <see cref="Normalize"/> makes the hash
/// independent of the checkout's line endings and of trailing blank lines.
/// </summary>
public static class AgentPromptResource
{
    /// <summary>Reads the embedded prompt whose logical path ends with <paramref name="path"/>
    /// (<c>Prompts/council/contract-analyst/v3.md</c>), as written (not normalized).</summary>
    public static string ReadEmbedded(Assembly assembly, string path)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The build turns '/' into '.' and folder-name hyphens into '_' in the manifest name; the
        // file name keeps its own. Compare on that common form.
        var wanted = "." + Canonical(path);
        var matches = assembly.GetManifestResourceNames()
            .Where(name => ("." + Canonical(name)).EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(matches.Count == 0
                ? $"Prompt file '{path}' is not an embedded resource of {assembly.GetName().Name}."
                : $"Prompt file '{path}' matches {matches.Count} embedded resources of {assembly.GetName().Name}: {string.Join(", ", matches)}.");
        }

        using var stream = assembly.GetManifestResourceStream(matches[0])
            ?? throw new InvalidOperationException($"Prompt resource '{matches[0]}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Line endings to <c>\n</c> and no trailing whitespace: the form that is hashed.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();
    }

    private static string Canonical(string name) =>
        name.Replace('/', '.').Replace('\\', '.').Replace('-', '_');
}
