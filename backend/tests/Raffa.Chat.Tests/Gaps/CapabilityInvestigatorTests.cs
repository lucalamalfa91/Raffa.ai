using System.Net;
using System.Text;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Jev;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Gaps;

/// <summary>ADR-031: the capability investigator decides, the server cleans and bounds what it
/// says, and every failure leaves the turn exactly as it was.</summary>
public sealed class CapabilityInvestigatorTests
{
    private const string ScreenshotQuestion = "puoi scrivere un report per riportare l'anamento del 2026 al CFO?";

    private static readonly IReadOnlyList<string> Suppliers = ["Splunk", "Amazon Web Services"];

    private static string Payload(
        string verdict,
        string confidence = "high",
        string knownGapKey = "",
        string nearest = "",
        string key = "",
        string titleEn = "",
        string titleIt = "",
        string operationEn = "",
        string operationIt = "",
        string descriptionEn = "",
        string descriptionIt = "",
        string[]? questions = null) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            rationale = "test",
            verdict,
            confidence,
            knownGapKey,
            nearestCapabilityKey = nearest,
            feature = new { key, titleEn, titleIt, operationEn, operationIt, descriptionEn, descriptionIt },
            alternativeQuestions = questions ?? [],
        });

    private static string GapPayload(string confidence = "high", string nearest = "portfolio") => Payload(
        "gap",
        confidence,
        nearest: nearest,
        key: "cfo-2026-spend-report",
        titleEn: "Spend reports for management",
        titleIt: "Report di spesa per il management",
        operationEn: "generate a spend report for management",
        operationIt: "generare un report di spesa per il management",
        descriptionEn: "Generate a periodic spend report for management.",
        descriptionIt: "Generare un report periodico sulla spesa per il management.",
        questions: ["Qual è la spesa annuale totale?", "Quali contratti Splunk scadono? https://evil.example"]);

    /// <summary>Jev off by default (<see cref="AiGatewayJevOptions.Enabled"/> defaults to
    /// <see langword="false"/>) -- every pre-existing test below keeps exercising exactly the same
    /// Foundry-only path it always has. <paramref name="jevHandler"/> lets the Jev-specific tests
    /// script a fake Jev HTTP response without ever touching a real network call.</summary>
    private static CapabilityInvestigator Investigator(
        IAiGateway gateway,
        GapInvestigationOptions? options = null,
        AiGatewayJevOptions? jevOptions = null,
        HttpMessageHandler? jevHandler = null)
    {
        var resolvedJevOptions = jevOptions ?? new AiGatewayJevOptions();
        var jevHttpClient = new JevHttpJsonClient(
            new HttpClient(jevHandler ?? new ScriptedJevHandler(HttpStatusCode.ServiceUnavailable, "{}")),
            resolvedJevOptions,
            new FoundryRetryPolicy(new AiGatewayResilienceOptions { MaxRetries = 0 }));
        var jevVerdictClient = new JevVerdictClient(jevHttpClient, resolvedJevOptions, SystemClock.Instance);

        return new(gateway, options ?? new GapInvestigationOptions(), resolvedJevOptions, jevVerdictClient);
    }

    /// <summary>Fake handler for the Jev-enabled tests: always answers with the same JSON body
    /// (built once from a <c>{"verdict": {...}, ...}</c> answers map) -- mirrors
    /// <c>Raffa.AiGateway.Tests.TestSupport.FakeHttpMessageHandler</c>'s own "no live network in
    /// unit tests" rule, duplicated locally rather than shared across test projects.</summary>
    private sealed class ScriptedJevHandler(HttpStatusCode statusCode, string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        public static string AnswersJson(
            string verdict, double confidence = 0.95, string? knownGapKey = null, string? nearestCapabilityKey = null)
        {
            var answers = new Dictionary<string, object>
            {
                ["verdict"] = new { choice = verdict, confidence },
            };
            if (knownGapKey is not null)
            {
                answers["knownGapKey"] = new { choice = knownGapKey, confidence = 0.9 };
            }

            if (nearestCapabilityKey is not null)
            {
                answers["nearestCapabilityKey"] = new { choice = nearestCapabilityKey, confidence = 0.9 };
            }

            return System.Text.Json.JsonSerializer.Serialize(new { answers });
        }
    }

    [Fact]
    public async Task A_gap_verdict_becomes_a_discovered_gap_with_server_authored_alternative()
    {
        var gateway = new ScriptedGateway(GapPayload());

        var result = await Investigator(gateway).InvestigateAsync(ScreenshotQuestion, Suppliers);

        Assert.Equal(GapVerdict.Gap, result.Verdict);
        Assert.Equal("gap", result.Outcome);
        var gap = Assert.IsType<CapabilityGap>(result.Gap);
        Assert.Equal(GapOrigin.Investigator, gap.Origin);
        Assert.Equal(GapAlternative.NearestCapability, gap.Alternative);
        Assert.Equal("discovered:cfo-spend-report", gap.Key);
        Assert.True(gap.Key.Length <= 40, "the key must fit feature_request.gap_key");
        Assert.Equal(CapabilityCatalog.PortfolioKey, gap.NearestCapabilityKey);
        Assert.False(gap.Matches(ScreenshotQuestion));
        Assert.Equal(CapabilityGapCopy.NearestAlternative(CapabilityCatalog.PortfolioKey, "it"), gap.AlternativeIt);
        Assert.StartsWith(
            "Al momento non posso generare un report di spesa per il management da Raffa.ai, però in Portfolio",
            CapabilityGapCopy.Preface(gap, "it"),
            StringComparison.Ordinal);

        var discovery = Assert.IsType<GapDiscovery>(gap.Discovery);
        Assert.Equal("Spend reports for management", discovery.TitleEn);
        Assert.Equal("high", discovery.Confidence);
        Assert.Equal(CapabilityInvestigatorAgent.Version, discovery.InvestigatorVersion);

        // The link never survives, a supplier name in a follow-up question may.
        Assert.Equal(["Qual è la spesa annuale totale?", "Quali contratti Splunk scadono?"], result.AlternativeQuestions);

        // The agent saw the question, its language and the whole capability map — never a supplier list.
        var input = gateway.LastInput;
        Assert.Contains("\"language\":\"it\"", input, StringComparison.Ordinal);
        Assert.Contains("\"knownGaps\"", input, StringComparison.Ordinal);
        Assert.Contains("\"askAbilities\"", input, StringComparison.Ordinal);
        Assert.Contains(CapabilityCatalog.QuoteCheckKey, input, StringComparison.Ordinal);
        Assert.DoesNotContain("Splunk", input, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feature_texts_lose_names_numbers_links_and_markup_before_anything_sees_them()
    {
        var gateway = new ScriptedGateway(Payload(
            "gap",
            nearest: "not-a-capability",
            key: "q3-report",
            titleEn: "**Splunk** Q3 2026 report",
            titleIt: "Report Q3 2026 su Amazon Web Services",
            operationEn: "send the report to cfo@acme.example",
            operationIt: "inviare il report da EUR 20k",
            descriptionEn: "Build the report described at https://example.com/spec for Splunk.",
            descriptionIt: "Costruire il report [1] per Splunk."));

        var result = await Investigator(gateway).InvestigateAsync(ScreenshotQuestion, Suppliers);

        var gap = Assert.IsType<CapabilityGap>(result.Gap);
        Assert.Equal("discovered:report", gap.Key);
        Assert.Equal("report", gap.TitleEn);
        Assert.Equal("Report su", gap.TitleIt);
        Assert.Equal("send the report to", gap.OperationEn);
        Assert.Equal("inviare il report da EUR", gap.OperationIt);
        Assert.Equal("Build the report described at for.", gap.DescriptionEn);
        Assert.Equal("Costruire il report per.", gap.DescriptionIt);
        Assert.Null(gap.NearestCapabilityKey);
        Assert.Equal(CapabilityGapCopy.NearestAlternative(null, "en"), gap.AlternativeEn);

        foreach (var text in new[] { gap.TitleEn, gap.TitleIt, gap.OperationEn, gap.OperationIt, gap.DescriptionEn, gap.DescriptionIt })
        {
            Assert.DoesNotMatch(@"\d", text);
            Assert.DoesNotContain("Splunk", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Amazon", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@", text, StringComparison.Ordinal);
            Assert.DoesNotContain("http", text, StringComparison.Ordinal);
            Assert.DoesNotContain("*", text, StringComparison.Ordinal);
            Assert.DoesNotContain("[", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_text_that_is_empty_once_cleaned_makes_the_discovery_unusable()
    {
        var gateway = new ScriptedGateway(Payload(
            "gap", key: "x", titleEn: "Splunk 2026", titleIt: "Report", operationEn: "do it", operationIt: "farlo",
            descriptionEn: "Do it.", descriptionIt: "Farlo."));

        var result = await Investigator(gateway).InvestigateAsync(ScreenshotQuestion, Suppliers);

        Assert.Equal(GapVerdict.None, result.Verdict);
        Assert.Equal("unusable", result.Outcome);
        Assert.Null(result.Gap);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("supported")]
    public async Task Question_and_supported_verdicts_find_nothing(string verdict)
    {
        var result = await Investigator(new ScriptedGateway(Payload(verdict))).InvestigateAsync("Which contracts renew next?", Suppliers);

        Assert.Equal(GapVerdict.Supported, result.Verdict);
        Assert.Equal(verdict, result.Outcome);
        Assert.Null(result.Gap);
    }

    [Fact]
    public async Task A_known_gap_verdict_resolves_to_the_catalog_entry_and_keeps_its_veto()
    {
        var payload = Payload("known-gap", knownGapKey: CapabilityGapCatalog.ReminderKey);

        var found = await Investigator(new ScriptedGateway(payload)).InvestigateAsync("Could you ping me a week ahead of the renewal?", Suppliers);
        Assert.Equal(GapVerdict.KnownGap, found.Verdict);
        Assert.Same(CapabilityGapCatalog.Find(CapabilityGapCatalog.ReminderKey), found.Gap);

        var vetoed = await Investigator(new ScriptedGateway(Payload("known-gap", knownGapKey: CapabilityGapCatalog.SendSupplierKey)))
            .InvestigateAsync("When must we send the notice to the supplier?", Suppliers);
        Assert.Equal(GapVerdict.None, vetoed.Verdict);
        Assert.Equal("unusable", vetoed.Outcome);

        var unknown = await Investigator(new ScriptedGateway(Payload("known-gap", knownGapKey: "teleport")))
            .InvestigateAsync("Teleport me", Suppliers);
        Assert.Equal(GapVerdict.None, unknown.Verdict);
    }

    [Fact]
    public async Task Confidence_below_the_threshold_never_hijacks_a_turn()
    {
        var low = await Investigator(new ScriptedGateway(GapPayload("low"))).InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal(GapVerdict.None, low.Verdict);
        Assert.Equal("low-confidence", low.Outcome);

        var strict = new GapInvestigationOptions { MinConfidence = "high" };
        var medium = await Investigator(new ScriptedGateway(GapPayload("medium")), strict).InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal(GapVerdict.None, medium.Verdict);

        var accepted = await Investigator(new ScriptedGateway(GapPayload("medium"))).InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal(GapVerdict.Gap, accepted.Verdict);
    }

    [Fact]
    public async Task Disabled_failed_malformed_and_slow_investigations_fail_open()
    {
        var disabledGateway = new ScriptedGateway(GapPayload());
        var disabled = await Investigator(disabledGateway, new GapInvestigationOptions { Enabled = false })
            .InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal("off", disabled.Outcome);
        Assert.Equal(0, disabledGateway.Calls);

        var failed = await Investigator(new ScriptedGateway(null)).InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal(GapVerdict.None, failed.Verdict);
        Assert.Equal("failed", failed.Outcome);
        Assert.Contains("simulated outage", failed.Failure, StringComparison.Ordinal);

        var malformed = await Investigator(new ScriptedGateway("not json")).InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal("failed", malformed.Outcome);

        var slow = await Investigator(new ScriptedGateway(GapPayload(), delay: TimeSpan.FromSeconds(10)), new GapInvestigationOptions { TimeoutSeconds = 1 })
            .InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal("failed", slow.Outcome);
        Assert.Contains("timed out", slow.Failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("supported")]
    public async Task Jev_enabled_question_and_supported_verdicts_never_call_Foundry(string verdict)
    {
        var foundry = new ScriptedGateway(GapPayload());
        var jevHandler = new ScriptedJevHandler(HttpStatusCode.OK, ScriptedJevHandler.AnswersJson(verdict));

        var result = await Investigator(foundry, jevOptions: new AiGatewayJevOptions { Enabled = true }, jevHandler: jevHandler)
            .InvestigateAsync("Which contracts renew next?", Suppliers);

        Assert.Equal(GapVerdict.Supported, result.Verdict);
        Assert.Equal(verdict, result.Outcome);
        Assert.Equal(0, foundry.Calls);
        Assert.Equal(1, jevHandler.Calls);
        Assert.Equal("typesafe/jev-1.13", result.Metadata!.ModelId);
    }

    [Fact]
    public async Task Jev_enabled_known_gap_verdict_never_calls_Foundry()
    {
        var foundry = new ScriptedGateway(GapPayload());
        var jevHandler = new ScriptedJevHandler(
            HttpStatusCode.OK,
            ScriptedJevHandler.AnswersJson("known-gap", knownGapKey: CapabilityGapCatalog.ReminderKey));

        var result = await Investigator(foundry, jevOptions: new AiGatewayJevOptions { Enabled = true }, jevHandler: jevHandler)
            .InvestigateAsync("Could you ping me a week ahead of the renewal?", Suppliers);

        Assert.Equal(GapVerdict.KnownGap, result.Verdict);
        Assert.Same(CapabilityGapCatalog.Find(CapabilityGapCatalog.ReminderKey), result.Gap);
        Assert.Equal(0, foundry.Calls);
    }

    [Fact]
    public async Task Jev_enabled_gap_verdict_calls_Foundry_only_to_write_the_feature_description()
    {
        var foundry = new ScriptedGateway(GapPayload(nearest: "renewals")); // Foundry's own verdict/nearest are ignored
        var jevHandler = new ScriptedJevHandler(
            HttpStatusCode.OK,
            ScriptedJevHandler.AnswersJson("gap", nearestCapabilityKey: CapabilityCatalog.PortfolioKey));

        var result = await Investigator(foundry, jevOptions: new AiGatewayJevOptions { Enabled = true }, jevHandler: jevHandler)
            .InvestigateAsync(ScreenshotQuestion, Suppliers);

        Assert.Equal(GapVerdict.Gap, result.Verdict);
        Assert.Equal(1, foundry.Calls);
        // Jev's nearestCapabilityKey wins, not Foundry's own ("renewals") on the same call.
        Assert.Equal(CapabilityCatalog.PortfolioKey, result.Gap!.NearestCapabilityKey);
        // The feature text itself still comes from Foundry -- Jev cannot write it.
        Assert.Equal("Spend reports for management", Assert.IsType<GapDiscovery>(result.Gap.Discovery).TitleEn);
    }

    [Fact]
    public async Task Jev_call_failure_falls_back_to_the_Foundry_only_path_unchanged()
    {
        var foundry = new ScriptedGateway(GapPayload());
        var jevHandler = new ScriptedJevHandler(HttpStatusCode.ServiceUnavailable, "{}");

        var result = await Investigator(foundry, jevOptions: new AiGatewayJevOptions { Enabled = true }, jevHandler: jevHandler)
            .InvestigateAsync(ScreenshotQuestion, Suppliers);

        Assert.Equal(GapVerdict.Gap, result.Verdict);
        Assert.Equal(1, foundry.Calls);
        Assert.Equal("scripted", result.Metadata!.ModelId); // Foundry's own metadata, not Jev's
    }

    [Fact]
    public async Task Jev_confidence_below_the_medium_threshold_finds_nothing_without_calling_Foundry()
    {
        var foundry = new ScriptedGateway(GapPayload());
        var jevHandler = new ScriptedJevHandler(HttpStatusCode.OK, ScriptedJevHandler.AnswersJson("gap", confidence: 0.2));

        var result = await Investigator(foundry, jevOptions: new AiGatewayJevOptions { Enabled = true }, jevHandler: jevHandler)
            .InvestigateAsync(ScreenshotQuestion, Suppliers);

        Assert.Equal(GapVerdict.None, result.Verdict);
        Assert.Equal("low-confidence", result.Outcome);
        Assert.Equal(0, foundry.Calls);
    }

    [Fact]
    public async Task The_fixture_double_finds_the_screenshot_gap_and_nothing_in_an_ordinary_question()
    {
        var fixture = new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());

        var gap = await Investigator(fixture).InvestigateAsync(ScreenshotQuestion, Suppliers);
        Assert.Equal(GapVerdict.Gap, gap.Verdict);
        Assert.Equal("discovered:management-report", gap.Gap!.Key);
        Assert.Equal(CapabilityCatalog.PortfolioKey, gap.Gap.NearestCapabilityKey);
        Assert.Equal(2, gap.AlternativeQuestions.Count);
        Assert.All(gap.AlternativeQuestions, q => Assert.EndsWith("?", q, StringComparison.Ordinal));

        foreach (var question in new[]
                 {
                     "Which contracts renew in the next 120 days?",
                     "Qual è la spesa annuale totale dei contratti?",
                     "Did you look at all my contracts?",
                     "What does the latest market report say about Splunk pricing?",
                 })
        {
            var ordinary = await Investigator(fixture).InvestigateAsync(question, Suppliers);
            Assert.Equal(GapVerdict.Supported, ordinary.Verdict);
        }
    }

    [Fact]
    public void The_schema_is_accepted_by_the_strict_structured_output_rules()
    {
        var verdict = StrictJsonSchemaValidator.Validate(CapabilityInvestigatorAgent.Schema);
        Assert.True(verdict.IsSuccess, verdict.IsFailure ? verdict.Error : null);
    }

    [Fact]
    public void Slugs_are_bounded_kebab_case_without_digits()
    {
        Assert.Equal("management-spend-report", DiscoveredGapText.Slug("Management Spend Report", null));
        Assert.Equal("report", DiscoveredGapText.Slug("2026-report", null));
        Assert.Equal("board-pack", DiscoveredGapText.Slug("__", "Board pack"));
        Assert.Equal("feature-request", DiscoveredGapText.Slug("", "42"));
        Assert.True(DiscoveredGapText.Slug("a-very-long-feature-key-that-keeps-going-and-going", null).Length <= DiscoveredGapText.MaxSlugLength);
    }

    /// <summary>Returns <c>payload</c> for the investigator (a failure when it is null), after an
    /// optional delay that honours cancellation the way a real HTTP call does.</summary>
    private sealed class ScriptedGateway(string? payload, TimeSpan? delay = null) : IAiGateway
    {
        public int Calls { get; private set; }

        public string LastInput { get; private set; } = string.Empty;

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CapabilityInvestigatorAgent.Name, request.AgentName);
            Assert.Equal(CapabilityInvestigatorAgent.Version, request.PromptVersion);
            Calls++;
            LastInput = request.InputJson;

            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            return payload is null
                ? Result<AiAnalysisResult>.Failure("simulated outage")
                : Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                    payload, new AiCallMetadata("scripted", "1", request.PromptVersion, DateTimeOffset.UtcNow, "hash")));
        }
    }
}
