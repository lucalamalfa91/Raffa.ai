using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests;

/// <summary>ADR-030 D3: the fixture's deterministic doubles for the drafting workflow's two agents
/// — an offer plan grounded in the input's own keys, and an email with no inline marker whose
/// numbers are the input's own values.</summary>
public sealed class FixtureAiGatewayAnalyzeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static FixtureAiGateway CreateGateway() => new(new AiGatewayModelOptions(), new FixedClock(Now));

    private const string Input =
        """
        {
          "question": "Puoi scrivermi la mail per il rinnovo ServiceNow?",
          "language": "it",
          "supplier": "ServiceNow",
          "goal": null,
          "items": [
            { "citationKey": "calc:savings-target", "corpus": "calc", "title": "ServiceNow — saving target", "subtitle": null, "snippet": "Target EUR 20000 is 8.7% of the annual spend.", "values": [ { "key": "targetAmount", "value": "20000", "kind": "Amount", "currency": "EUR" } ] },
            { "citationKey": "calc:council:play[1]", "corpus": "calc", "title": "Play 1 — Market discount", "subtitle": null, "snippet": "Ask for the 9% discount. Timing: before the deadline. Grounded in: calc:lever[x].", "values": [ { "key": "percent", "value": "9", "kind": "Percentage", "currency": null } ] },
            { "citationKey": "fact:x:renewal", "corpus": "tenant", "title": "ServiceNow · OrderForm", "subtitle": null, "snippet": "Ends on 2028-04-10.", "values": [ { "key": "cancellationDeadline", "value": "2027-10-13", "kind": "Date", "currency": null } ] },
            { "citationKey": "raffa:playbook:anchor-on-market", "corpus": "raffa", "title": "Anchor the ask", "subtitle": null, "snippet": "Open with the market median. When to use: always. Ask: \"Comparable customers closed at the median.\"", "values": [] }
          ],
          "plan": { "position": "", "asks": [ { "lever": "Market discount", "sentence": "Ask for the 9% discount.", "citationKeys": ["calc:council:play[1]"] } ], "trade": "", "deadlineAnchor": "", "closing": "" }
        }
        """;

    [Fact]
    public async Task Planner_agent_returns_an_offer_plan_grounded_in_input_keys()
    {
        var result = await CreateGateway().AnalyzeAsync(new AiAnalysisRequest("offer-planner", "prompt", Input, "{}", "draft-v1"));

        Assert.True(result.IsSuccess);
        using var payload = JsonDocument.Parse(result.Value.PayloadJson);
        var root = payload.RootElement;

        Assert.StartsWith("Target EUR 20000", root.GetProperty("position").GetString(), StringComparison.Ordinal);
        var ask = Assert.Single(root.GetProperty("asks").EnumerateArray());
        Assert.Equal("Ask for the 9% discount.", ask.GetProperty("sentence").GetString());
        Assert.Equal("calc:council:play[1]", ask.GetProperty("citationKeys")[0].GetString());
        Assert.Equal("Comparable customers closed at the median.", root.GetProperty("trade").GetString());
        Assert.Equal("2027-10-13", root.GetProperty("deadlineAnchor").GetString());
        Assert.Equal("draft-v1", result.Value.Metadata.PromptVersion);
    }

    [Fact]
    public async Task Writer_agent_returns_subject_body_and_used_keys_with_no_inline_marker()
    {
        var result = await CreateGateway().AnalyzeAsync(new AiAnalysisRequest("negotiation-writer", "prompt", Input, "{}", "draft-v1"));

        Assert.True(result.IsSuccess);
        using var payload = JsonDocument.Parse(result.Value.PayloadJson);
        var root = payload.RootElement;

        var subject = root.GetProperty("subject").GetString()!;
        var body = root.GetProperty("body").GetString()!;
        Assert.Equal("Rinnovo ServiceNow: proposta di revisione", subject);
        Assert.StartsWith("Gentile team ServiceNow,", body, StringComparison.Ordinal);
        Assert.Contains("EUR 20000", body, StringComparison.Ordinal);
        Assert.Contains("- Ask for the 9% discount.", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\[\d+\]", body);
        Assert.DoesNotContain("Grounded in:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("calc:", body, StringComparison.Ordinal);

        var used = root.GetProperty("usedCitationKeys").EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.Contains("calc:savings-target", used);
        Assert.Contains("calc:council:play[1]", used);
    }
}
