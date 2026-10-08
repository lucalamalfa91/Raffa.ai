using System.Text.Json;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Conversations;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// F3-D03 — the history a later turn's prompts see never carries web text. A web-research reply is
/// public, <em>unverified</em> prose (ADR-030); it is shown to the user with its label, but if it were
/// fed back as an earlier turn the <c>answer</c> role would read it as established context, with the
/// label gone, and could restate it as fact. So when the last turns of a conversation are handed to
/// the Ask engine, a Raffa turn that is web's — a <c>web</c>-corpus citation, or a research prompt
/// version in its provenance — is cut:
/// <list type="bullet">
/// <item>a combined reply (contracts answer + web section, ADR-032) keeps only its contracts half;</item>
/// <item>a web-only reply (answer, abstain or refusal of the research role) is replaced by a neutral
/// <see cref="Placeholder"/>, so the turn order and the user's own question stay intact.</item>
/// </list>
/// The stored conversation and what the user sees are untouched. Pure.
/// </summary>
public static class WebHistoryIsolation
{
    /// <summary>Stands in for a web-only reply in the prompt history. Carries no web content.</summary>
    public const string Placeholder = "[An earlier reply built from public web research is not repeated here.]";

    private const string WebCorpus = "web";
    private const string ResearchPromptVersionMarker = "research-";

    /// <summary>The last <paramref name="limit"/> messages, oldest first, with every web turn cut.</summary>
    public static IReadOnlyList<ConversationMessageResult> ForPrompt(
        IReadOnlyList<ConversationMessageResult> messages, int limit)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages
            .TakeLast(Math.Max(0, limit))
            .Select(Isolate)
            .ToList();
    }

    /// <summary>Whether a stored message is a web-research turn (a Raffa row only).</summary>
    public static bool IsWebTurn(ConversationMessageResult message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Role != ConversationRole.Raffa)
        {
            return false;
        }

        return HasWebCitation(message.CitationsJson)
            || (message.PromptVersion?.Contains(ResearchPromptVersionMarker, StringComparison.OrdinalIgnoreCase) ?? false)
            || ContainsWebSection(message.Markdown);
    }

    private static ConversationMessageResult Isolate(ConversationMessageResult message)
    {
        if (!IsWebTurn(message))
        {
            return message;
        }

        // A combined reply: its contracts half is Raffa's own, grounded text — keep exactly that.
        var cut = WebSectionStart(message.Markdown);
        if (cut > 0)
        {
            var contractsHalf = message.Markdown[..cut].TrimEnd();
            if (contractsHalf.Length > 0)
            {
                return message with { Markdown = contractsHalf };
            }
        }

        return message with { Markdown = Placeholder };
    }

    private static int WebSectionStart(string markdown)
    {
        var best = -1;
        foreach (var italian in new[] { true, false })
        {
            var index = markdown.IndexOf($"**{WebModeReplyBuilder.WebSectionTitle(italian)}**", StringComparison.Ordinal);
            if (index >= 0 && (best < 0 || index < best))
            {
                best = index;
            }
        }

        return best;
    }

    private static bool ContainsWebSection(string markdown) => WebSectionStart(markdown) >= 0;

    private static bool HasWebCitation(string? citationsJson)
    {
        if (string.IsNullOrWhiteSpace(citationsJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(citationsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var citation in document.RootElement.EnumerateArray())
            {
                if (citation.ValueKind == JsonValueKind.Object
                    && citation.TryGetProperty("corpus", out var corpus)
                    && corpus.ValueKind == JsonValueKind.String
                    && string.Equals(corpus.GetString(), WebCorpus, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Not parseable: it is not a citation list this module wrote; treat as non-web.
        }

        return false;
    }
}
