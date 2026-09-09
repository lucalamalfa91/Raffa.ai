using Contigo.AiGateway.Contracts;
using Contigo.Chat.Application.Capabilities;
using Contigo.Chat.Application.Pack;

namespace Contigo.Chat.Application.Reply;

/// <summary>
/// Assembles a <see cref="CopilotReply"/> from a guarded `answer`-role result (task
/// E13/F06/US01/T01, ask-engine; ADR-024 §6). The one place <see cref="ReplyCitation"/> rows are
/// ever constructed from a <see cref="PackItem"/> — every other builder in this namespace
/// (<see cref="RedirectReplyBuilder"/>) either cites nothing or cites a caller-supplied,
/// already-real citation built the same way.
/// </summary>
public static class CopilotReplyBuilder
{
    /// <summary>
    /// Turns a guarded <see cref="AiAnswerResult"/> (already validated by
    /// <c>Guards.GroundingGuard</c>/<c>Guards.NumericGuard</c>, and downgraded by
    /// <c>Guards.RegenerateOnce</c> when it failed twice) into a <see cref="CopilotReply"/>.
    /// <see cref="ReplyKind.Answer"/> when <see cref="AiAnswerResult.CanDetermine"/> is
    /// <see langword="true"/>, <see cref="ReplyKind.Abstain"/> otherwise (R-ASK-07).
    /// </summary>
    /// <param name="guarded">The final, guard-approved (or guard-downgraded) result.</param>
    /// <param name="pack">The same pack the gateway was given — resolves every
    /// <see cref="AiAnswerResult.CitationKeys"/> entry back to its full citation card.</param>
    /// <param name="actions">Already-resolved actions for this turn's
    /// <see cref="AiAnswerResult.ActionKeys"/> (via <c>CapabilityRouting.ResolveActions</c> in the
    /// composition root — this builder never resolves an action key itself).</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static CopilotReply FromGuardedResult(
        AiAnswerResult guarded, IReadOnlyList<PackItem> pack, IReadOnlyList<CopilotAction> actions)
    {
        ArgumentNullException.ThrowIfNull(guarded);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(actions);

        if (!guarded.CanDetermine)
        {
            return new CopilotReply(
                ReplyKind.Abstain,
                guarded.AbstainReason ?? "Nothing in the validated contracts supports a reliable answer.",
                [],
                [],
                new ReplyProvenance([], guarded.Metadata.ModelId, guarded.Metadata.PromptVersion, guarded.Metadata.InputHash),
                []);
        }

        var citations = BuildCitations(guarded.CitationKeys ?? [], pack);
        var sources = citations.Select(c => c.Corpus).Distinct(StringComparer.Ordinal).ToList();

        return new CopilotReply(
            ReplyKind.Answer,
            guarded.AnswerMarkdown ?? string.Empty,
            citations,
            actions,
            new ReplyProvenance(sources, guarded.Metadata.ModelId, guarded.Metadata.PromptVersion, guarded.Metadata.InputHash),
            guarded.FollowUps ?? []);
    }

    /// <summary>
    /// Resolves <paramref name="citationKeys"/> (already proven, by
    /// <c>Guards.GroundingGuard</c>, to exist in <paramref name="pack"/>) into
    /// <see cref="ReplyCitation"/> rows, numbered in the order the model returned them — the same
    /// order its inline <c>[n]</c> markers reference.
    /// </summary>
    public static IReadOnlyList<ReplyCitation> BuildCitations(
        IReadOnlyList<string> citationKeys, IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(citationKeys);
        ArgumentNullException.ThrowIfNull(pack);

        var byKey = pack.ToDictionary(item => item.CitationKey, StringComparer.Ordinal);

        var citations = new List<ReplyCitation>();
        for (var i = 0; i < citationKeys.Count; i++)
        {
            // Defensive only: a caller that skipped GroundingGuard could hand this an unresolved
            // key. Silently dropped rather than throwing — one bad key must not break every other
            // (already-grounded) citation in the same reply.
            if (!byKey.TryGetValue(citationKeys[i], out var item))
            {
                continue;
            }

            citations.Add(new ReplyCitation(
                N: i + 1,
                Corpus: item.Corpus,
                Title: item.Title,
                Subtitle: item.Subtitle,
                Snippet: item.Snippet,
                DocumentId: item.Corpus == PackCorpus.Tenant ? item.CitationKey : null,
                ContractId: null,
                Page: item.Page,
                Section: item.Section,
                PreviewUrl: item.PreviewUrl,
                Href: item.Href,
                RecordId: item.RecordId));
        }

        return citations;
    }
}
