using System.Text.Json;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Fixtures;

/// <summary>
/// The deterministic double of Ask Raffa's orchestrator (<see cref="IFixtureAgentScript"/>): what a
/// well-behaved model would do, with no network and no randomness, so the API tests and the golden
/// set can run the agentic path. Reads the tool results already in the conversation and decides the
/// next step from the user's last message alone:
/// <list type="number">
/// <item>no tool result yet → <c>list_portfolio</c>;</item>
/// <item>the portfolio read, and the message names one of its suppliers (or the state's primary
/// supplier is given) → <c>get_contract</c> on it, plus <c>get_market_benchmark</c> when the message
/// talks about the market, or <c>compose_artifact</c> when it asks to build a KPI/dashboard/report;</item>
/// <item>otherwise the final answer: the first cited items of every tool result, in the fixture
/// gateway's own "[n] title: values" form, so every figure is a pack figure.</item>
/// </list>
/// Greetings, small talk and legal questions get a short final answer with no tool call, as the
/// persona's law 5 says.
/// </summary>
public sealed partial class AskAgentFixtureScript : IFixtureAgentScript
{
    private const int MaxCitedItems = 3;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Result<AiConversationResult> Next(AiConversationRequest request, AiCallMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(request);
        var toolNames = request.Tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var lastUser = request.Messages.LastOrDefault(m => m.Role == AiConversationMessage.UserRole)?.Content ?? string.Empty;
        var toolResults = request.Messages.Where(m => m.Role == AiConversationMessage.ToolRole).ToList();
        var calledNames = request.Messages.Where(m => m.ToolCalls is not null).SelectMany(m => m.ToolCalls!).Select(c => c.Name).ToList();

        if (toolResults.Count == 0 && IsSmallTalk(lastUser))
        {
            return Final(ItalianPattern().IsMatch(lastUser)
                ? "Ciao! Sono Raffa, il tuo copilota per contratti, rinnovi e risparmi. Chiedimi di un contratto, di una scadenza o di dove risparmiare."
                : "Hi! I'm Raffa, your copilot for contracts, renewals and savings. Ask me about a contract, a deadline or where to save.",
                [], null, metadata);
        }

        if (!request.ForceFinal && toolResults.Count == 0 && toolNames.Contains("list_portfolio"))
        {
            return Calls(metadata, ("list_portfolio", """{"focus":"all"}"""));
        }

        var suppliers = SuppliersFromResults(toolResults);
        var primary = PrimaryFromPrompt(request.SystemPrompt);
        var named = suppliers.FirstOrDefault(s => lastUser.Contains(s, StringComparison.OrdinalIgnoreCase))
            ?? (primary is not null && suppliers.Contains(primary, StringComparer.OrdinalIgnoreCase) ? primary : null);

        if (!request.ForceFinal && named is not null && !calledNames.Contains("get_contract") && toolNames.Contains("get_contract"))
        {
            var calls = new List<(string, string)> { ("get_contract", JsonSerializer.Serialize(new { supplier = named, question = lastUser })) };
            if (MarketPattern().IsMatch(lastUser) && toolNames.Contains("get_market_benchmark"))
            {
                calls.Add(("get_market_benchmark", JsonSerializer.Serialize(new { supplier = named, question = lastUser })));
            }

            if (ArtifactPattern().IsMatch(lastUser) && CreatePattern().IsMatch(lastUser) && toolNames.Contains("compose_artifact"))
            {
                var kind = KpiPattern().IsMatch(lastUser) ? "kpi" : DocumentPattern().IsMatch(lastUser) ? "document" : "dashboard";
                calls.Add(("compose_artifact", JsonSerializer.Serialize(new
                {
                    kind, supplier = named, request = lastUser, usesMarket = MarketPattern().IsMatch(lastUser), usesPortfolio = false,
                })));
            }

            return Calls(metadata, calls.ToArray());
        }

        // Final answer from what the tools returned.
        var items = ItemsFromResults(toolResults).Take(MaxCitedItems).ToList();
        var artifactSummary = toolResults.Select(ArtifactTitle).FirstOrDefault(t => t is not null);
        if (artifactSummary is not null)
        {
            return Final(ItalianPattern().IsMatch(lastUser)
                ? $"Ho creato “{artifactSummary}” e l'ho aperto nel pannello azioni."
                : $"I created “{artifactSummary}” and opened it in the action panel.",
                [], named, metadata);
        }

        if (items.Count == 0)
        {
            return Result<AiConversationResult>.Success(new AiConversationResult([], JsonSerializer.Serialize(new
            {
                canDetermine = false,
                answerMarkdown = "",
                citationKeys = Array.Empty<string>(),
                actionKeys = Array.Empty<string>(),
                followUps = Array.Empty<string>(),
                abstainReason = ItalianPattern().IsMatch(lastUser)
                    ? "Non trovo questo dato nei contratti validati. Indica il fornitore o carica il contratto in Documenti."
                    : "I cannot find that in the validated contracts. Name the supplier or upload the contract in Documents.",
                focusSupplier = named,
            }, Json), metadata));
        }

        var lines = items.Select((item, i) => $"[{i + 1}] {item.Title}: {(item.Values.Count > 0 ? string.Join(", ", item.Values.Select(v => v.Key + " " + v.Value)) : item.Snippet)}.");
        return Final(string.Join(" ", lines), items.Select(i => i.CitationKey).ToList(), named, metadata);
    }

    private static Result<AiConversationResult> Calls(AiCallMetadata metadata, params (string Name, string Arguments)[] calls) =>
        Result<AiConversationResult>.Success(new AiConversationResult(
            calls.Select((c, i) => new AiToolCall($"call-{i + 1}-{c.Name}", c.Name, c.Arguments)).ToList(), null, metadata));

    private static Result<AiConversationResult> Final(string markdown, IReadOnlyList<string> citationKeys, string? focus, AiCallMetadata metadata) =>
        Result<AiConversationResult>.Success(new AiConversationResult([], JsonSerializer.Serialize(new
        {
            canDetermine = true,
            answerMarkdown = markdown,
            citationKeys,
            actionKeys = Array.Empty<string>(),
            followUps = Array.Empty<string>(),
            abstainReason = (string?)null,
            focusSupplier = focus,
        }, Json), metadata));

    private static IReadOnlyList<string> SuppliersFromResults(IEnumerable<AiConversationMessage> toolResults)
    {
        var names = new List<string>();
        foreach (var message in toolResults)
        {
            try
            {
                using var document = JsonDocument.Parse(message.Content ?? "{}");
                if (document.RootElement.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object
                    && summary.TryGetProperty("contracts", out var contracts) && contracts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contract in contracts.EnumerateArray())
                    {
                        if (contract.TryGetProperty("supplier", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } name && name != "?")
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ToList();
    }

    private static IEnumerable<(string CitationKey, string Title, string Snippet, IReadOnlyList<(string Key, string Value)> Values)> ItemsFromResults(
        IEnumerable<AiConversationMessage> toolResults)
    {
        foreach (var message in toolResults)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(message.Content ?? "{}");
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    var key = item.TryGetProperty("citationKey", out var k) ? k.GetString() : null;
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    var values = new List<(string, string)>();
                    if (item.TryGetProperty("values", out var vs) && vs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var v in vs.EnumerateArray())
                        {
                            values.Add((v.GetProperty("key").GetString() ?? "", v.GetProperty("value").GetString() ?? ""));
                        }
                    }

                    yield return (key, item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "", item.TryGetProperty("snippet", out var sn) ? sn.GetString() ?? "" : "", values);
                }
            }
        }
    }

    private static string? ArtifactTitle(AiConversationMessage message)
    {
        try
        {
            using var document = JsonDocument.Parse(message.Content ?? "{}");
            return document.RootElement.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object
                && summary.TryGetProperty("built", out var built) && built.ValueKind == JsonValueKind.True
                && summary.TryGetProperty("title", out var title) ? title.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? PrimaryFromPrompt(string systemPrompt)
    {
        var match = PrimaryPattern().Match(systemPrompt);
        return match.Success && match.Groups[1].Value != "none" ? match.Groups[1].Value : null;
    }

    private static bool IsSmallTalk(string text) => SmallTalkPattern().IsMatch(text) && !ContractWordPattern().IsMatch(text);

    [GeneratedRegex(@"primarySupplier=([^;]+);", RegexOptions.CultureInvariant)]
    private static partial Regex PrimaryPattern();

    [GeneratedRegex(@"\b(mercato|market|benchmark|concorren\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarketPattern();

    [GeneratedRegex(@"\b(kpi|dashboard|report|brief|documento|document|grafic\w*|chart)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactPattern();

    [GeneratedRegex(@"\b(crea\w*|costruisci\w*|genera\w*|prepara\w*|fammi|make|create|build|prepare|generate)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreatePattern();

    [GeneratedRegex(@"\b(kpi)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KpiPattern();

    [GeneratedRegex(@"\b(report|brief|documento|document|relazione)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentPattern();

    [GeneratedRegex(@"^\s*(ciao|salve|buongiorno|buonasera|hello|hi|hey|good\s+(morning|afternoon|evening))\b|\b(ricetta|carbonara|meteo|weather|calcio|football|barzelletta|joke|nonna|grandma)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SmallTalkPattern();

    [GeneratedRegex(@"\b(contratt\w*|contract\w*|rinnov\w*|renew\w*|scad\w*|expir\w*|risparm\w*|saving\w*|fornitor\w*|supplier\w*|clausol\w*|clause\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContractWordPattern();

    [GeneratedRegex(@"\b(il|la|di|che|non|per|con|una|uno|come|quando|quanto|ciao|scade|contratto|crea|mercato)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItalianPattern();
}
