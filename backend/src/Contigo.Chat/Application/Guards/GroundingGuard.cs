using System.Text.RegularExpressions;
using Contigo.AiGateway.Contracts;
using Contigo.Chat.Application.Capabilities;
using Contigo.Chat.Application.Pack;

namespace Contigo.Chat.Application.Guards;

/// <summary>
/// The ADR-024 grounding guard (task E13/F06/US01/T01, ask-engine coding objective point 5: "every
/// `[n]` / citationKey in the pack; every actionKey resolvable"; `inputs/requirements.md` R-ASK-06
/// point 1 + point 3). Combines three checks a caller (<c>Answering.AnswerComposer</c>) must run
/// before ever showing or persisting a model's answer:
/// <list type="number">
/// <item>Every <see cref="AiAnswerResult.CitationKeys"/> entry — and every inline <c>[n]</c> marker
/// in <see cref="AiAnswerResult.AnswerMarkdown"/> — is grounded in the pack (delegated to
/// <see cref="AbstainGuard.Enforce(AiAnswerResult, IReadOnlyList{PackItem})"/>, so the citation
/// -key logic is defined in exactly one place).</item>
/// <item>Every inline <c>[n]</c> marker resolves to a position <see cref="AiAnswerResult.CitationKeys"/>
/// actually has (never a marker pointing past the end of the list the model itself returned).</item>
/// <item>Every <see cref="AiAnswerResult.ActionKeys"/> entry resolves to a real
/// <see cref="Capabilities.CapabilityCatalog"/> key — "hrefs are never model-authored" (R-SYS-02):
/// the model may only ever name a bare capability key, never a URL, and even that key must be real.</item>
/// </list>
/// Pure and synchronous — no I/O, no LLM call (Appendix C rule 6).
/// </summary>
public static class GroundingGuard
{
    // A markdown inline citation marker, e.g. "[1]", "[12]" — deliberately not "[^\]]*" (any
    // bracketed text): a reference/footnote-style link target ("[text][ref]") or a literal
    // bracketed word must never be misread as a citation index.
    private static readonly Regex InlineCitationPattern = new(@"\[(\d+)\]", RegexOptions.Compiled);

    /// <summary>
    /// Validates <paramref name="result"/> against <paramref name="pack"/>. Returns the first
    /// violation found (citation grounding, then inline-marker range, then actionKey resolution) —
    /// callers only ever need to know the guard failed and why, not every independent way it did.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> or <paramref name="pack"/>
    /// is <see langword="null"/>.</exception>
    public static GuardVerdict Validate(AiAnswerResult result, IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(pack);

        if (!result.CanDetermine)
        {
            // Already an honest abstention (or the caller's own downgrade) — nothing further to
            // ground; AbstainGuard.Enforce would return the identical no-op verdict.
            return GuardVerdict.Ok;
        }

        var citationOutcome = new AbstainGuard().Enforce(result, pack);
        if (citationOutcome.Intervened)
        {
            return GuardVerdict.Fail(citationOutcome.Reason!);
        }

        var citationKeyCount = result.CitationKeys?.Count ?? 0;
        var markdown = result.AnswerMarkdown ?? string.Empty;

        foreach (Match match in InlineCitationPattern.Matches(markdown))
        {
            var n = int.Parse(match.Groups[1].Value);
            if (n < 1 || n > citationKeyCount)
            {
                return GuardVerdict.Fail(
                    $"inline citation marker '[{n}]' has no matching citationKey — the model " +
                    $"returned only {citationKeyCount} (Appendix C rule 10).");
            }
        }

        foreach (var actionKey in result.ActionKeys ?? [])
        {
            if (CapabilityCatalog.Find(actionKey) is null)
            {
                return GuardVerdict.Fail(
                    $"actionKey '{actionKey}' does not resolve to any capability in the catalog — " +
                    "hrefs/actions are never model-authored (R-SYS-02).");
            }
        }

        return GuardVerdict.Ok;
    }
}
