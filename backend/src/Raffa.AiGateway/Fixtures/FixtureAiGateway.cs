using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Fixtures;

/// <summary>
/// Deterministic, provider-free <see cref="IAiGateway"/> implementation. No Foundry account or
/// Document Intelligence endpoint exists in this environment yet — there is no
/// <c>infra/modules</c> Terraform module for Azure AI services, and no Foundry connection string
/// anywhere in <c>appsettings*.json</c> — so a live-provider implementation would have nothing to
/// call. ADR-004 "Implications for the decomposition" explicitly allows this: "until then a
/// fixture gateway adapter satisfies R0 scaffolding" (echoed for the `ocr` role by ADR-017, and
/// for Benchmark Service by the module-map: "fixture adapter is enough for first demo").
///
/// This fixture still exercises the full contract a real implementation must honour: config
/// -selected model ids flow into every <see cref="AiCallMetadata"/> (AC-1), classification
/// returns a type and a confidence (AC-2), and every call computes
/// model/version/prompt-version/timestamp/input-hash itself rather than leaving it to the caller
/// (ADR-011). A later task swaps this for a real Foundry-backed implementation behind the same
/// <see cref="IAiGateway"/> seam — domain code never notices (AC-3: "Domain code calls only
/// IAiGateway").
/// </summary>
public sealed class FixtureAiGateway(
    AiGatewayModelOptions modelOptions, IClock clock, AiGatewayOcrOptions? ocrOptions = null) : IAiGateway
{
    /// <summary>
    /// Not a real Foundry prompt version — there is no live prompt behind this fixture. Recorded
    /// so every <see cref="AiCallMetadata"/> is fully populated and callers/tests can assert on
    /// it, per ADR-011's reproducibility fields.
    /// </summary>
    private const string PromptVersion = "fixture-v1";

    /// <summary>
    /// Optional trailing constructor parameter (default <see langword="null"/>, resolved to
    /// <see cref="AiGatewayOcrOptions"/>'s own defaults below) so every existing call site that
    /// constructs this type with the original two arguments — every test written before task
    /// E02/F01/US02/T02, plus <see cref="ServiceCollectionExtensions.AddAiGatewayModule"/>'s own
    /// registration, which resolves this via DI (a container matches this parameter to the
    /// <see cref="AiGatewayOcrOptions"/> singleton that same method now also registers) — keeps
    /// compiling unchanged.
    /// </summary>
    private readonly AiGatewayOcrOptions _ocrOptions = ocrOptions ?? new AiGatewayOcrOptions();

    /// <summary>
    /// Placeholder text for a page this fixture cannot decode (genuine binary content — a real
    /// scanned image/PDF). Named so a test/log line can recognize "this is the fixture's honest
    /// placeholder", not a corrupted real extraction (mirrors <see cref="ExtractAsync"/>'s own
    /// "{}" placeholder — see that method's doc comment).
    /// </summary>
    private const string BinaryContentPlaceholder =
        "[fixture-ocr: {0} bytes of binary content; no live Document Intelligence endpoint configured]";

    /// <summary>
    /// Ordered, case-insensitive substring cues the fixture uses to pick a
    /// <see cref="AiDocumentType"/> deterministically. Checked in order; the first match wins.
    /// Not a real classifier — a real Foundry model replaces this entirely, prompted against the
    /// full <see cref="AiDocumentType"/> taxonomy (ADR-004 candidate: "Small instruction model...
    /// classification is low-complexity").
    /// </summary>
    /// <summary>Image container signatures a fixture "scanned image" may start with — see
    /// <see cref="DecodePages"/>.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary><c>%PDF-</c>: a PDF is read by <see cref="FixturePdfTextScanner"/> (ADR-017 amendment
    /// 2026-09-09: in production every PDF goes through Document Intelligence, so the fixture must
    /// read the repo's hand-built test PDFs itself to stay provider-free).</summary>
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46, 0x2D];

    private static readonly (AiDocumentType Type, string Keyword)[] ClassificationKeywords =
    [
        (AiDocumentType.Msa, "MASTER SERVICES AGREEMENT"),
        (AiDocumentType.Msa, "MSA"),
        (AiDocumentType.OrderForm, "ORDER FORM"),
        (AiDocumentType.Sow, "STATEMENT OF WORK"),
        (AiDocumentType.Sow, "SOW"),
        (AiDocumentType.Amendment, "AMENDMENT"),
        (AiDocumentType.Quote, "QUOTE"),
        (AiDocumentType.Invoice, "INVOICE"),
        (AiDocumentType.PriceList, "PRICE LIST"),
        (AiDocumentType.Nda, "NON-DISCLOSURE AGREEMENT"),
        (AiDocumentType.Nda, "NDA"),
        (AiDocumentType.Dpa, "DATA PROCESSING AGREEMENT"),
        (AiDocumentType.Dpa, "DPA"),
    ];

    /// <inheritdoc/>
    public Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentText))
        {
            return Task.FromResult(
                Result<AiClassificationResult>.Failure("Classification requires non-empty document text."));
        }

        var upperText = request.DocumentText.ToUpperInvariant();

        var documentType = AiDocumentType.Other;
        var confidence = 0.5;

        foreach (var (type, keyword) in ClassificationKeywords)
        {
            if (upperText.Contains(keyword, StringComparison.Ordinal))
            {
                documentType = type;
                confidence = 0.99;
                break;
            }
        }

        var result = new AiClassificationResult(
            documentType,
            confidence,
            BuildMetadata(modelOptions.Classify, request.DocumentText));

        return Task.FromResult(Result<AiClassificationResult>.Success(result));
    }

    /// <inheritdoc/>
    public Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentText))
        {
            return Task.FromResult(
                Result<AiExtractionResult>.Failure("Extraction requires non-empty document text."));
        }

        if (string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            return Task.FromResult(Result<AiExtractionResult>.Failure(
                "Extraction requires a target JSON schema (spec §7.3: schema-constrained output)."));
        }

        // No live structured-output model behind this fixture. Until the deployed environments
        // gain a real Foundry account this fixture is what processes every upload, so it must
        // produce facts that are true of the document rather than an empty placeholder: a
        // deterministic, rule-based read of the page-marked text — every value quoted from the
        // text, every span the literal match, every confidence a statement about the rule that
        // fired (an explicit cue, a derived value, or a conflict the text does not resolve). See
        // FixtureContractFactExtractor for the rules and for why this is honest, not fabricated:
        // a document without a fee sentence gets no annualSpend; a preamble that does not say
        // which party supplies gets a low-confidence supplier proposal a reviewer must confirm.
        var payload = FixtureContractFactExtractor.Extract(request.StageName, request.DocumentText);

        var result = new AiExtractionResult(
            payload,
            BuildMetadata(modelOptions.Extract, request.StageName + " " + request.DocumentText + " " + request.JsonSchema));

        return Task.FromResult(Result<AiExtractionResult>.Success(result));
    }

    /// <inheritdoc/>
    public Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Task.FromResult(Result<AiEmbeddingResult>.Failure("Embedding requires non-empty text."));
        }

        var vector = DeterministicPseudoEmbedding(request.Text);

        var result = new AiEmbeddingResult(
            vector,
            BuildMetadata(modelOptions.Embed, request.Text));

        return Task.FromResult(Result<AiEmbeddingResult>.Success(result));
    }

    /// <inheritdoc/>
    public Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Task.FromResult(Result<AiAnswerResult>.Failure("A question is required."));
        }

        // Task E13/F06/US01/T01 (ask-engine): deterministic ADR-024 v2 behaviour when the caller
        // supplied a context pack — the only branch this task's own file scope permits touching in
        // Raffa.AiGateway ("this is the only AiGateway edit this phase" — that task's own Files
        // table). Checked before the legacy Evidence-only branch below so a caller that supplies
        // both (today no caller does) still gets the pack-grounded, no-tools v2 shape ADR-024
        // requires; a caller that supplies neither falls through to the pre-existing
        // empty-evidence abstain path unchanged.
        if (!string.IsNullOrWhiteSpace(request.PackJson))
        {
            return Task.FromResult(AnswerFromPack(request));
        }

        if (request.Evidence.Count == 0)
        {
            // Appendix C rule 10 / ADR-004: abstain rather than fabricate. ADR-011 puts
            // authorization + retrieval upstream of the gateway, so an empty evidence list means
            // authorized retrieval genuinely found nothing — "cannot determine" is the only
            // honest response, not a failure.
            var abstained = new AiAnswerResult(
                CanDetermine: false,
                Answer: null,
                Citations: [],
                Metadata: BuildMetadata(modelOptions.Answer, request.Question));

            return Task.FromResult(Result<AiAnswerResult>.Success(abstained));
        }

        var citations = request.Evidence
            .Select(evidence => new AiCitation(evidence.DocumentId, evidence.Page, evidence.Section))
            .ToList();

        // No live grounded-generation model behind this fixture yet. The evidence text is
        // surfaced verbatim rather than paraphrased — never say more than the (test) evidence
        // actually contains.
        var answerText = string.Join(" ", request.Evidence.Select(evidence => evidence.Text));

        var grounded = new AiAnswerResult(
            CanDetermine: true,
            Answer: answerText,
            Citations: citations,
            Metadata: BuildMetadata(modelOptions.Answer, request.Question + " " + answerText));

        return Task.FromResult(Result<AiAnswerResult>.Success(grounded));
    }

    /// <summary>
    /// ADR-024 v2 deterministic echo (task E13/F06/US01/T01, ask-engine coding objective: "give
    /// FixtureAiGateway.AnswerAsync a deterministic v2 behaviour when a pack is supplied — echo a
    /// markdown that cites the first <see cref="MaxCitedPackItems"/> pack keys and copies their
    /// values verbatim — no chunk concatenation — so the guards and the golden set run without
    /// Foundry"). Deserializes <see cref="AiAnswerRequest.PackJson"/> into this fixture's own
    /// minimal, string-typed shape (<see cref="FixturePackItem"/>/<see cref="FixturePackValue"/>) —
    /// this project cannot reference <c>Raffa.Chat.Application.Pack.PackItem</c> at all (ADR-002
    /// dependency direction runs the other way: <c>Raffa.Chat</c> depends on
    /// <c>Raffa.AiGateway</c>, never the reverse), so the two shapes are independently declared
    /// and agree only by JSON field name (both produced/read with
    /// <see cref="JsonSerializerDefaults.Web"/> camelCase + <see cref="JsonStringEnumConverter"/>).
    /// Unlike <see cref="AnswerAsync"/>'s legacy evidence branch (a single
    /// <see cref="string.Join(string, IEnumerable{string})"/> concatenation of every evidence
    /// chunk into one run-on paragraph), each cited pack item gets its own numbered line — "no
    /// chunk concatenation" (task text, verbatim).
    /// </summary>
    private Result<AiAnswerResult> AnswerFromPack(AiAnswerRequest request)
    {
        List<FixturePackItem>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<FixturePackItem>>(request.PackJson!, PackJsonOptions);
        }
        catch (JsonException ex)
        {
            return Result<AiAnswerResult>.Failure($"PackJson was not valid JSON: {ex.Message}");
        }

        items ??= [];

        if (items.Count == 0)
        {
            // Same "authorized retrieval/composition genuinely found nothing" honesty as the
            // legacy empty-Evidence branch above (Appendix C rule 10) — an empty pack is valid
            // input, not an error.
            var abstained = new AiAnswerResult(
                CanDetermine: false,
                Answer: null,
                Citations: [],
                Metadata: BuildMetadata(modelOptions.Answer, request.Question),
                AnswerMarkdown: null,
                CitationKeys: [],
                ActionKeys: [],
                AbstainReason: "The context pack is empty — nothing to answer from.",
                FollowUps: []);

            return Result<AiAnswerResult>.Success(abstained);
        }

        var citedCount = Math.Min(items.Count, MaxCitedPackItems);
        var citationKeys = new List<string>(citedCount);
        var lines = new List<string>(citedCount);

        for (var i = 0; i < citedCount; i++)
        {
            var item = items[i];
            var valueText = item.Values is { Count: > 0 }
                ? string.Join(", ", item.Values.Select(FormatFixturePackValue))
                : item.Snippet;

            lines.Add($"[{i + 1}] {item.Title}: {valueText}.");
            citationKeys.Add(item.CitationKey);
        }

        var answerMarkdown = string.Join(" ", lines);

        var result = new AiAnswerResult(
            CanDetermine: true,
            Answer: answerMarkdown,
            Citations: [],
            Metadata: BuildMetadata(modelOptions.Answer, request.Question + " " + answerMarkdown),
            AnswerMarkdown: answerMarkdown,
            CitationKeys: citationKeys,
            ActionKeys: [],
            AbstainReason: null,
            FollowUps: []);

        return Result<AiAnswerResult>.Success(result);
    }

    /// <summary>Verbatim copy of the pack value, formatted so it round-trips through
    /// <c>Raffa.Chat.Application.Guards.NumericGuard</c>'s own token patterns: an amount as
    /// <c>"{currency} {value}"</c>, a percentage as <c>"{value}%"</c>, a date or bare number as
    /// the stored value unchanged.</summary>
    private static string FormatFixturePackValue(FixturePackValue value) => value.Kind switch
    {
        "Amount" => $"{value.Currency} {value.Value}",
        "Percentage" => $"{value.Value}%",
        _ => value.Value,
    };

    /// <summary>Task text: "cites the first N pack keys" — N fixed at 5, generous enough to prove
    /// a multi-citation reply without echoing an unbounded pack verbatim.</summary>
    private const int MaxCitedPackItems = 5;

    /// <summary>Same camelCase-plus-string-enum shape
    /// <c>Raffa.Chat.Application.Answering.AnswerComposer</c> serializes
    /// <c>Raffa.Chat.Application.Pack.PackItem</c> with — see this type's own <c>PackJsonOptions</c>
    /// doc comment for why both sides must agree on this exact convention despite neither
    /// referencing the other's type.</summary>
    private static readonly JsonSerializerOptions PackJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>This fixture's own minimal, string-typed mirror of
    /// <c>Raffa.Chat.Application.Pack.PackItem</c> — see <see cref="AnswerFromPack"/>'s own doc
    /// comment for why a structural (JSON field name) match is the only contract between the two,
    /// not a shared type.</summary>
    private sealed record FixturePackItem(
        string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet,
        IReadOnlyList<FixturePackValue>? Values);

    /// <summary>Mirrors <c>Raffa.Chat.Application.Pack.PackValue</c> — <see cref="Kind"/> is the
    /// enum's own name string ("Amount"/"Percentage"/"Date"/"Number"), never re-typed as the
    /// Raffa.Chat enum this project cannot reference.</summary>
    private sealed record FixturePackValue(string Key, string Value, string Kind, string? Currency);

    /// <inheritdoc/>
    /// <summary>
    /// The `analyst` role, deterministic: reads the council's own input shape (structural JSON
    /// contract with <c>Raffa.Chat.Application.Council</c>, the same way <see cref="FixturePackItem"/>
    /// mirrors the pack) and returns a payload that any consumer can ground — every finding or play
    /// cites an input item's own citation key and reuses that item's own text, so the numbers it
    /// carries are the pack's numbers. An agent whose name ends in "analyst" gets findings; any
    /// other agent gets plays plus a verdict.
    /// </summary>
    public Task<Result<AiAnalysisResult>> AnalyzeAsync(
        AiAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return Task.FromResult(Result<AiAnalysisResult>.Failure("Analysis requires an agent name."));
        }

        if (string.IsNullOrWhiteSpace(request.InputJson))
        {
            return Task.FromResult(Result<AiAnalysisResult>.Failure("Analysis requires a non-empty JSON input."));
        }

        FixtureAnalysisInput? input;
        try
        {
            input = JsonSerializer.Deserialize<FixtureAnalysisInput>(request.InputJson, PackJsonOptions);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(Result<AiAnalysisResult>.Failure($"InputJson was not valid JSON: {ex.Message}"));
        }

        var items = input?.Items ?? [];
        var isAnalyst = request.AgentName.EndsWith("analyst", StringComparison.OrdinalIgnoreCase);
        var isPlanner = request.AgentName.EndsWith("planner", StringComparison.OrdinalIgnoreCase);
        var isWriter = request.AgentName.EndsWith("writer", StringComparison.OrdinalIgnoreCase);

        string payload;
        if (string.Equals(request.AgentName, MarketResearcherAgentName, StringComparison.OrdinalIgnoreCase))
        {
            // The market researcher's double: one same-supplier query named after the first contract
            // item, for a commercial question only (a legal reading or a date lookup gets none).
            var question = input?.Question ?? string.Empty;
            var first = items.FirstOrDefault(i => string.Equals(i.Corpus, "tenant", StringComparison.OrdinalIgnoreCase))
                ?? items.FirstOrDefault(i => i.Title.Contains('·', StringComparison.Ordinal));
            var queries = !CommercialWordPattern.IsMatch(question)
                ? []
                : first is not null
                    ? new[] { new { query = first.Title.Replace('·', ' ').Trim(), scope = "same-supplier" } }
                    : new[] { new { query = question.Trim(), scope = "similar-contracts" } };
            payload = JsonSerializer.Serialize(new { queries }, PackJsonOptions);
        }
        else if (string.Equals(request.AgentName, CapabilityInvestigatorAgentName, StringComparison.OrdinalIgnoreCase))
        {
            payload = BuildFixtureInvestigation(input);
        }
        else if (isPlanner)
        {
            payload = BuildFixtureOfferPlan(items);
        }
        else if (isWriter)
        {
            payload = BuildFixtureEmailDraft(input!, items);
        }
        else if (isAnalyst)
        {
            var findings = items.Take(MaxFixtureFindings).Select(item => new
            {
                title = item.Title,
                insight = item.Snippet,
                leverType = (string?)null,
                citationKeys = new[] { item.CitationKey },
            });
            payload = JsonSerializer.Serialize(new { findings }, PackJsonOptions);
        }
        else
        {
            var leverItems = items.Where(i => i.CitationKey.StartsWith("calc:lever", StringComparison.Ordinal)).ToList();
            var source = leverItems.Count > 0 ? leverItems : items;
            var plays = source.Take(MaxFixtureFindings).Select((item, index) => new
            {
                rank = index + 1,
                lever = item.Title,
                ask = item.Snippet,
                expectedValueKeys = (item.Values ?? []).Select(v => v.Key).ToArray(),
                fallback = "If refused, hold the notice reservation and keep the alternative on the table.",
                timing = "Before the notice deadline.",
                citationKeys = new[] { item.CitationKey },
            });

            var targetItem = items.FirstOrDefault(i => i.CitationKey is "calc:savings-target" or "calc:portfolio-target");
            var reachable = targetItem?.Subtitle?.Contains("reachable", StringComparison.OrdinalIgnoreCase) == true
                && targetItem.Subtitle?.Contains("not", StringComparison.OrdinalIgnoreCase) != true;
            var verdict = new
            {
                targetReachable = reachable,
                reason = targetItem?.Snippet ?? "No target was named.",
            };
            payload = JsonSerializer.Serialize(new { plays, verdict }, PackJsonOptions);
        }

        var result = new AiAnalysisResult(
            payload,
            BuildMetadata(modelOptions.Analyst ?? modelOptions.Answer, request.AgentName + " " + request.InputJson) with { PromptVersion = request.PromptVersion });

        return Task.FromResult(Result<AiAnalysisResult>.Success(result));
    }

    private const int MaxFixtureFindings = 3;

    /// <summary>The offer planner's deterministic double (ADR-030 D3): the position is the target
    /// verdict's own snippet, the asks are the council's plays (or the lever items) quoted up to
    /// their "Timing:/Fallback:/Grounded in:" trail, the trade is the first playbook entry's
    /// quotable ask, the deadline anchor the first date value — every field either verbatim pack
    /// text or empty, so nothing here can carry a number the pack does not.</summary>
    private static string BuildFixtureOfferPlan(IReadOnlyList<FixturePackItem> items)
    {
        var plays = items.Where(i => i.CitationKey.StartsWith("calc:council:play[", StringComparison.Ordinal)).ToList();
        var source = plays.Count > 0
            ? plays
            : items.Where(i => i.CitationKey.StartsWith("calc:lever[", StringComparison.Ordinal)).ToList();

        var asks = source.Take(MaxFixtureFindings).Select(item => new
        {
            lever = item.Title,
            sentence = QuotableAsk(item.Snippet),
            citationKeys = new[] { item.CitationKey },
        });

        var position = (items.FirstOrDefault(i => i.CitationKey == "calc:savings-target")
            ?? items.FirstOrDefault(i => i.CitationKey == "calc:when-you-must-move"))?.Snippet ?? string.Empty;
        var trade = items
            .Where(i => i.CitationKey.StartsWith("raffa:playbook:", StringComparison.Ordinal))
            .Select(i => PlaybookAsk(i.Snippet))
            .FirstOrDefault(a => a is not null) ?? string.Empty;
        var deadline = items
            .SelectMany(i => i.Values ?? [])
            .FirstOrDefault(v => v.Kind == "Date")?.Value ?? string.Empty;

        return JsonSerializer.Serialize(new { position, asks, trade, deadlineAnchor = deadline, closing = string.Empty }, PackJsonOptions);
    }

    /// <summary>The negotiation writer's deterministic double: a greeting, one line per cited
    /// fact item (title plus its values, formatted the way the numeric guard reads them back —
    /// <see cref="FormatFixturePackValue"/>), the plan's asks verbatim, a fixed closing. No
    /// inline marker, no link, no key in the text; <c>usedCitationKeys</c> lists what it used.</summary>
    private static string BuildFixtureEmailDraft(FixtureAnalysisInput input, IReadOnlyList<FixturePackItem> items)
    {
        var supplier = string.IsNullOrWhiteSpace(input.Supplier) ? "supplier" : input.Supplier.Trim();
        var italian = string.Equals(input.Language, "it", StringComparison.OrdinalIgnoreCase);
        var used = new List<string>();
        var lines = new List<string>
        {
            italian ? $"Gentile team {supplier}," : $"Dear {supplier} team,",
            string.Empty,
            italian
                ? "vi scrivo in merito al rinnovo del nostro contratto e alle condizioni che vorremmo rivedere prima di confermarlo."
                : "I am writing about the renewal of our contract and the terms we would like to revise before confirming it.",
            string.Empty,
        };

        foreach (var item in items.Where(i => i.Values is { Count: > 0 }).Take(4))
        {
            lines.Add($"- {item.Title}: {string.Join(", ", item.Values!.Select(FormatFixturePackValue))}.");
            used.Add(item.CitationKey);
        }

        foreach (var ask in input.Plan?.Asks ?? [])
        {
            if (string.IsNullOrWhiteSpace(ask.Sentence))
            {
                continue;
            }

            lines.Add("- " + ask.Sentence.Trim());
            used.AddRange(ask.CitationKeys ?? []);
        }

        lines.Add(string.Empty);
        lines.Add(italian
            ? "Restiamo disponibili a un confronto e vi chiediamo una proposta aggiornata prima della scadenza."
            : "We remain available to discuss this and ask for a revised proposal before the deadline.");
        lines.Add(string.Empty);
        lines.Add(italian ? "Cordiali saluti," : "Kind regards,");
        lines.Add(italian ? "[Nome e cognome]" : "[Name and surname]");

        if (used.Count == 0 && items.Count > 0)
        {
            used.Add(items[0].CitationKey);
        }

        var payload = new
        {
            subject = italian ? $"Rinnovo {supplier}: proposta di revisione" : $"{supplier} renewal: revised proposal",
            body = string.Join("\n", lines),
            usedCitationKeys = used.Distinct(StringComparer.Ordinal).ToArray(),
        };

        return JsonSerializer.Serialize(payload, PackJsonOptions);
    }

    /// <summary>A council play's snippet up to its " Timing:" / " Fallback:" / " Grounded in:"
    /// trail — the same cut <c>Raffa.Chat.Application.Drafting.DraftPlan.AskSentence</c> makes
    /// (duplicated: this project cannot reference Raffa.Chat).</summary>
    private static string QuotableAsk(string snippet)
    {
        var cut = snippet.Length;
        foreach (var marker in new[] { " Timing:", " Fallback:", " Grounded in:" })
        {
            var index = snippet.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        return snippet[..cut].Trim();
    }

    private static string? PlaybookAsk(string snippet)
    {
        const string Marker = " Ask: ";
        var index = snippet.IndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var ask = snippet[(index + Marker.Length)..].Trim().Trim('"').Trim();
        return ask.Length == 0 ? null : ask;
    }

    /// <summary>Structural mirror of the analyst/planner/writer inputs
    /// (<c>Raffa.Chat.Application.Council.NegotiationCouncil</c> and
    /// <c>Raffa.Chat.Application.Drafting.NegotiationDraftingWorkflow</c>): only the fields the
    /// doubles read; every other field of the real input is ignored.</summary>
    private sealed record FixtureAnalysisInput(
        IReadOnlyList<FixturePackItem>? Items,
        string? Supplier = null,
        string? Language = null,
        FixturePlan? Plan = null,
        string? Question = null);

    private sealed record FixturePlan(IReadOnlyList<FixturePlanAsk>? Asks);

    private sealed record FixturePlanAsk(string? Lever, string? Sentence, IReadOnlyList<string>? CitationKeys);

    /// <summary>The Ask flow's market researcher (<c>Raffa.Chat.Application.Council.CouncilAgents
    /// .MarketResearcherName</c> — this project cannot reference it, ADR-002).</summary>
    private const string MarketResearcherAgentName = "market-researcher";

    /// <summary>ADR-031's capability investigator (<c>Raffa.Chat.Application.Gaps
    /// .CapabilityInvestigatorAgent.Name</c>).</summary>
    private const string CapabilityInvestigatorAgentName = "capability-investigator";

    // The investigator double's one discovered gap: a request (a verb or "can you") for a
    // deliverable no screen produces — a report, a presentation, slides, a dashboard, a chart.
    private static readonly Regex FixtureDeliverablePattern = new(
        @"\b(report\w*|presentazion\w*|presentation\w*|slides?|deck|dashboard\w*|grafic[oi]|charts?|graphs?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FixtureRequestCuePattern = new(
        @"\b(puoi|potresti|riesci|can\s+you|could\s+you|would\s+you|please|per\s+favore|scriv\w*|write|prepar\w*|" +
        @"fammi|make|build|crea\w*|genera\w*|generate|produc\w*|produrre|mi\s+serve|ho\s+bisogno|i\s+need)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The capability investigator's deterministic double (ADR-031): a request for a report-like
    /// deliverable is the one discovered gap — generic texts with no name and no number, Portfolio
    /// as the nearest screen, two questions Ask already answers in the question's language —
    /// and every other message is an ordinary <c>question</c>. The real agent judges any phrasing;
    /// this double only has to be stable for CI and the golden set.
    /// </summary>
    private static string BuildFixtureInvestigation(FixtureAnalysisInput? input)
    {
        var question = input?.Question ?? string.Empty;
        var italian = string.Equals(input?.Language, "it", StringComparison.OrdinalIgnoreCase);
        var isGap = FixtureDeliverablePattern.IsMatch(question) && FixtureRequestCuePattern.IsMatch(question);

        var emptyFeature = new
        {
            key = string.Empty,
            titleEn = string.Empty,
            titleIt = string.Empty,
            operationEn = string.Empty,
            operationIt = string.Empty,
            descriptionEn = string.Empty,
            descriptionIt = string.Empty,
        };

        if (!isGap)
        {
            return JsonSerializer.Serialize(
                new
                {
                    rationale = "The message asks for information Ask can give in the chat.",
                    verdict = "question",
                    confidence = "high",
                    knownGapKey = string.Empty,
                    nearestCapabilityKey = string.Empty,
                    feature = emptyFeature,
                    alternativeQuestions = Array.Empty<string>(),
                },
                PackJsonOptions);
        }

        return JsonSerializer.Serialize(
            new
            {
                rationale = "The message asks for a formatted report, which no screen or Ask ability produces.",
                verdict = "gap",
                confidence = "high",
                knownGapKey = string.Empty,
                nearestCapabilityKey = "portfolio",
                feature = new
                {
                    key = "management-report",
                    titleEn = "Management reports",
                    titleIt = "Report per il management",
                    operationEn = "generate a report for management",
                    operationIt = "generare un report per il management",
                    descriptionEn = "Generate a periodic report on contracts, spend and savings, ready to share with management.",
                    descriptionIt = "Generare un report periodico su contratti, spesa e risparmi, pronto da condividere con il management.",
                },
                alternativeQuestions = italian
                    ? new[] { "Qual è la spesa annuale totale dei contratti?", "Quali sono i contratti più critici e dove possiamo risparmiare?" }
                    : new[] { "What is our total annual spend across contracts?", "Which contracts are most critical and where can we save?" },
            },
            PackJsonOptions);
    }

    private static readonly Regex CommercialWordPattern = new(
        @"\b(pric\w*|prezz\w*|cost\w*|spen[dt]\w*|spes\w*|save|saves|saving|savings|risparm\w*|renew\w*|rinnov\w*|" +
        @"scadenz\w*|discount\w*|scont\w*|budget|quarter\w*|trimestr\w*|annual\w*|e-?mail|mail|negotiat\w*|" +
        @"negozia\w*|levers?|leve|market|mercato|benchmark\w*|quotes?|preventiv\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProcurementWordPattern = new(
        @"\b(contract\w*|contratt\w*|renewal\w*|rinnov\w*|supplier\w*|fornitor\w*|procurement|negotiat\w*|negozia\w*|" +
        @"licen[cs]\w*|saas|cloud|pricing|prezz\w*|price\w*|discount\w*|scont\w*|uplift|notice|preavviso|vendor\w*|" +
        @"benchmark\w*|market|mercato|tender|gara|sla|subscription|insurance|assicura\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>ADR-030: the CI double of the research role. Two fixed public sources with snippets
    /// that carry every figure the summary quotes (so <c>NumericGuard</c> grounds them), an
    /// off-topic refusal when the query has no procurement word, never a real HTTP call.</summary>
    public Task<Result<AiResearchResult>> ResearchAsync(
        AiResearchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Task.FromResult(Result<AiResearchResult>.Failure("Research requires a non-empty query."));
        }

        var model = modelOptions.Research ?? new AiModelSelection("fixture-research", "fixture");
        var offTopic = !ProcurementWordPattern.IsMatch(request.Query);

        IReadOnlyList<AiWebSource> sources = offTopic
            ? []
            :
            [
                new AiWebSource(
                    "https://example.com/procurement/saas-renewals",
                    "SaaS renewal benchmarks — example.com",
                    "Typical enterprise SaaS renewals close with a 5-10% uplift cap and 60 to 90 days of notice."),
                new AiWebSource(
                    "https://example.org/negotiation/levers",
                    "Negotiation levers buyers cite most — example.org",
                    "Multi-year commitments and volume tiers are the levers buyers cite most often."),
            ];

        var summary = offTopic
            ? string.Empty
            : "Public sources describe a 5-10% uplift cap on enterprise SaaS renewals [1] and multi-year " +
              "commitments as the lever buyers cite most often [2]. Nothing here is verified against your contracts.";

        return Task.FromResult(Result<AiResearchResult>.Success(
            new AiResearchResult(
                summary,
                sources,
                offTopic,
                BuildMetadata(model, request.Query) with { PromptVersion = request.PromptVersion })));
    }

    public Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Content.IsEmpty)
        {
            return Task.FromResult(Result<AiOcrResult>.Failure("Ocr requires non-empty document content."));
        }

        var pages = DecodePages(request.Content.Span);

        // ADR-017: "over-budget jobs fail visibly (failed status), they are not silently
        // truncated" — checked here, inside the one role every OCR call flows through
        // (AiGatewayOcrOptions's own doc comment: "single choke point"), so no caller can bypass
        // it. A real (non-fixture) implementation would run this same check against the page
        // count Document Intelligence actually reports.
        if (pages.Count > _ocrOptions.MaxPagesPerDocument)
        {
            return Task.FromResult(Result<AiOcrResult>.Failure(
                $"OCR page budget exceeded: document '{request.FileName}' has {pages.Count} pages, " +
                $"configured maximum is {_ocrOptions.MaxPagesPerDocument} (ADR-017: fail visibly, " +
                "never silently truncate)."));
        }

        var result = new AiOcrResult(pages, BuildMetadata(modelOptions.Ocr, request.Content.Span));

        return Task.FromResult(Result<AiOcrResult>.Success(result));
    }

    /// <summary>
    /// No live Document Intelligence endpoint behind this fixture yet (ADR-017 "Implications for
    /// the decomposition": "Fixture OCR is allowed for R0" — the same rule ADR-004 states for the
    /// other four roles, extended by ADR-017 to this one). Deterministically decodes
    /// <paramref name="content"/> as UTF-8 text and splits it on the form-feed character
    /// (<c>\f</c>, U+000C) — a conventional plain-text page-break marker — so tests/callers can
    /// exercise multi-page OCR output without a real scanned-image parser. Content that is not
    /// valid UTF-8 text (genuine binary — a real scanned image/PDF) still returns one honest
    /// placeholder page rather than fabricating plausible-looking contract text, the same "empty
    /// JSON is honest" choice <see cref="ExtractAsync"/> already makes for its own placeholder.
    ///
    /// Every page this returns has a <see langword="null"/> <see cref="AiOcrPage.Words"/> (ADR-017
    /// w18): there is no `prebuilt-layout` behind this fixture either, and fabricating plausible
    /// pixel polygons would be exactly the dishonesty this method already refuses for text. CI
    /// therefore proves the box-overlay code paths tolerate "no geometry" against every fixture
    /// document, never against a real Document Intelligence Layout response — matching this
    /// project's existing "CI stays provider-free" posture for the other four roles.
    /// </summary>
    private static IReadOnlyList<AiOcrPage> DecodePages(ReadOnlySpan<byte> content)
    {
        // Task E13/F04/US01/T01 (documents-admission): POST /api/documents now admits an image only
        // when its bytes carry a real PNG/JPEG signature, so a fixture "scanned image" is that
        // signature followed by UTF-8 page text (form-feed separated) — the signature is stripped
        // here and the remainder decoded exactly as before. A real photo (binary after the
        // signature) still fails strict UTF-8 decoding and gets the honest placeholder below.
        if (content.StartsWith(PdfSignature))
        {
            // A born-digital PDF (the shape every hand-built test fixture in this repo has): the
            // scanner pairs each page with its text-bearing content stream; anything it cannot
            // pair (a scanned PDF, object streams, CID fonts) gets the honest placeholder below.
            var pdfPages = FixturePdfTextScanner.TryExtractPages(content);
            if (pdfPages is not null)
            {
                return pdfPages.Select((text, index) => new AiOcrPage(index + 1, text)).ToList();
            }

            return [new AiOcrPage(1, string.Format(CultureInfo.InvariantCulture, BinaryContentPlaceholder, content.Length))];
        }

        var payload = content;
        if (payload.StartsWith(PngSignature))
        {
            payload = payload[PngSignature.Length..];
        }
        else if (payload.StartsWith(JpegSignature))
        {
            payload = payload[JpegSignature.Length..];
        }

        string decoded;
        try
        {
            decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            return [new AiOcrPage(1, string.Format(CultureInfo.InvariantCulture, BinaryContentPlaceholder, content.Length))];
        }

        var pageTexts = decoded.Split('\f');
        var pages = new List<AiOcrPage>(pageTexts.Length);

        for (var i = 0; i < pageTexts.Length; i++)
        {
            pages.Add(new AiOcrPage(i + 1, pageTexts[i]));
        }

        return pages;
    }

    private AiCallMetadata BuildMetadata(AiModelSelection model, string input)
    {
        var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return new AiCallMetadata(model.ModelId, model.ModelVersion, PromptVersion, clock.UtcNow, inputHash);
    }

    /// <summary>Byte-input twin of <see cref="BuildMetadata(AiModelSelection, string)"/> — the
    /// `ocr` role's input is already bytes (see <see cref="AiOcrRequest.Content"/>'s own doc
    /// comment), so hashing it directly avoids a lossy/re-encoding round trip through
    /// <see cref="string"/> for content that may not even be valid text.</summary>
    private AiCallMetadata BuildMetadata(AiModelSelection model, ReadOnlySpan<byte> input)
    {
        var inputHash = Convert.ToHexString(SHA256.HashData(input));
        return new AiCallMetadata(model.ModelId, model.ModelVersion, PromptVersion, clock.UtcNow, inputHash);
    }

    /// <summary>
    /// Deterministic, seedless pseudo-embedding: the same input text always yields the same
    /// vector, and different text yields a (practically certain to be) different one — enough for
    /// fixture-level tests around dimension and determinism without a live embedding model.
    /// Dimension fixed at <see cref="AiGatewayConstants.EmbeddingDimensions"/> to match
    /// <c>Raffa.Documents.Contracts.Domain.Embedding.VectorDimensions</c> (ADR-004: "dimension
    /// fixed at schema time... text-embedding-3-small").
    /// </summary>
    private static float[] DeterministicPseudoEmbedding(string text)
    {
        var seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vector = new float[AiGatewayConstants.EmbeddingDimensions];

        for (var i = 0; i < vector.Length; i++)
        {
            var b = seedBytes[i % seedBytes.Length];

            // Map a byte (0-255) into [-1, 1], the range typical of normalized embeddings, so
            // consumers exercising vector math (e.g. cosine similarity) get sane fixture inputs.
            vector[i] = (b / 255f * 2f) - 1f;
        }

        return vector;
    }
}
