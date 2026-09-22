namespace Raffa.Chat.Application.Capabilities;

/// <summary>
/// How a <see cref="CopilotAction"/> should read/behave — R-SYS-04's replacement rule turns what
/// would otherwise be an ordinary "open this screen" action into an explicit upload prompt, and a
/// reply consumer (a later task's markdown/UI renderer) needs to tell the two apart (e.g. an
/// upload affordance vs a plain deep link) without parsing the label text.
/// </summary>
public enum CopilotActionKind
{
    /// <summary>Opens an existing screen/route — the ordinary case.</summary>
    Navigate,

    /// <summary>Prompts the caller to upload a contract in Documents — either because the question
    /// named a supplier with no validated contract yet (R-SYS-02 "unknown supplier → Upload in
    /// Documents"), or because R-SYS-04 replaced a greyed capability's action.</summary>
    Upload,

    /// <summary>Opens an absolute https URL outside the app in a new tab — today only the GitHub
    /// issue a feedback submission opened (ADR-030 D6). Server-authored from the publisher's own
    /// response, never model-authored and never built from a catalog route.</summary>
    External,
}

/// <summary>
/// One suggested action in an Ask reply (ADR-024 "reply `{ kind, answerMarkdown, citations[],
/// actions[], provenance, followUps[] }`"; R-SYS-02 "Intents map to actions with real hrefs from
/// the catalog only"). Produced only by <see cref="CapabilityRouting.ResolveActions"/> — never
/// hand-built elsewhere, so every <see cref="Href"/> is traceable back to a
/// <see cref="Capability.RoutePattern"/> plus, at most, one known object id.
/// </summary>
/// <param name="Label">Button text.</param>
/// <param name="Href">A complete route — never contains an unresolved `{...}` placeholder (see
/// <see cref="CapabilityRouting.ResolveActions"/>'s own doc comment).</param>
/// <param name="Kind">See <see cref="CopilotActionKind"/>.</param>
public sealed record CopilotAction(string Label, string Href, CopilotActionKind Kind)
{
    /// <summary>The one sanctioned way to build a <see cref="CopilotActionKind.External"/> action
    /// (ADR-030 D6): the href must be an absolute https URL, so a relative route or a plain-http
    /// link can never be rendered as an outbound anchor.</summary>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute https URL.</exception>
    public static CopilotAction External(string label, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("An external action needs an absolute https URL.", nameof(url));
        }

        return new CopilotAction(label, uri.ToString(), CopilotActionKind.External);
    }
}
