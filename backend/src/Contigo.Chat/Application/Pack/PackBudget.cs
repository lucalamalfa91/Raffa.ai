namespace Contigo.Chat.Application.Pack;

/// <summary>
/// Config-bound token budget for one assembled context pack (task E13/F06/US01/T01, ask-engine
/// coding objective point 3: "PackBudget (Chat:PackTokenBudget)"; parent story "Council decisions
/// carried into this story": "Pack token budget Chat:PackTokenBudget"). "Pack size is bounded
/// (token budget per role, configurable)" (R-ASK-04) — <see cref="Apply"/> is the one place that
/// bound is enforced, so every caller (the composition root) gets a pack that already fits inside
/// the `answer` role's own context window regardless of how many tenant/market/calc items it
/// assembled.
/// </summary>
public sealed class PackBudget
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "Chat:PackTokenBudget";

    /// <summary>
    /// Starting default when no <c>Chat:PackTokenBudget</c> configuration is present — generous
    /// enough for a multi-item pack (structured facts, clause chunks, market notes, calculator
    /// output) against the cheap instruction models ADR-004 selects, cheap enough that a runaway
    /// pack still fails fast rather than silently ballooning the `answer` role's cost (R-AI-04).
    /// </summary>
    public const int DefaultMaxTokens = 4000;

    /// <summary>Rough, deliberately conservative characters-per-token ratio for a token estimate
    /// with no tokenizer dependency in this module (<c>Contigo.Chat</c>'s allow-list is exactly
    /// <c>[SharedKernel, AiGateway]</c> — no tokenizer library is referenced here). Underestimating
    /// tokens-per-character (i.e. a smaller divisor) is the safe direction: it trims more
    /// aggressively than a real tokenizer would need, never less.</summary>
    private const double CharactersPerToken = 3.5;

    public PackBudget(int? maxTokens = null)
    {
        MaxTokens = maxTokens is > 0 ? maxTokens.Value : DefaultMaxTokens;
    }

    /// <summary>This instance's configured maximum, always a positive number of tokens.</summary>
    public int MaxTokens { get; }

    /// <summary>
    /// Returns as many leading <paramref name="items"/> (in caller-supplied priority order — the
    /// composition root orders items most-relevant-first) as fit within <see cref="MaxTokens"/>,
    /// estimated from each item's own <see cref="PackItem.Snippet"/>/<see cref="PackItem.Title"/>
    /// length. Never returns zero items when <paramref name="items"/> is non-empty: the single
    /// most relevant item is always kept even if it alone exceeds the budget (a pack that abstains
    /// because its one candidate item is verbose is worse than a slightly over-budget call —
    /// R-ASK-04's "bounded" pack must not become an accidentally-empty one).
    /// </summary>
    public IReadOnlyList<PackItem> Apply(IReadOnlyList<PackItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return items;
        }

        var kept = new List<PackItem>();
        var usedTokens = 0;

        foreach (var item in items)
        {
            var itemTokens = EstimateTokens(item);

            if (kept.Count > 0 && usedTokens + itemTokens > MaxTokens)
            {
                break;
            }

            kept.Add(item);
            usedTokens += itemTokens;
        }

        return kept;
    }

    private static int EstimateTokens(PackItem item)
    {
        var characterCount = item.Title.Length + (item.Subtitle?.Length ?? 0) + item.Snippet.Length +
            item.Values.Sum(v => v.Key.Length + v.Value.Length);

        return Math.Max(1, (int)Math.Ceiling(characterCount / CharactersPerToken));
    }
}
