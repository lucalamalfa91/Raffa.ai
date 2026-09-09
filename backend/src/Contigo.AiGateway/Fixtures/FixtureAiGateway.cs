using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Fixtures;

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
        // Contigo.AiGateway ("this is the only AiGateway edit this phase" — that task's own Files
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
    /// this project cannot reference <c>Contigo.Chat.Application.Pack.PackItem</c> at all (ADR-002
    /// dependency direction runs the other way: <c>Contigo.Chat</c> depends on
    /// <c>Contigo.AiGateway</c>, never the reverse), so the two shapes are independently declared
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
    /// <c>Contigo.Chat.Application.Guards.NumericGuard</c>'s own token patterns: an amount as
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
    /// <c>Contigo.Chat.Application.Answering.AnswerComposer</c> serializes
    /// <c>Contigo.Chat.Application.Pack.PackItem</c> with — see this type's own <c>PackJsonOptions</c>
    /// doc comment for why both sides must agree on this exact convention despite neither
    /// referencing the other's type.</summary>
    private static readonly JsonSerializerOptions PackJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>This fixture's own minimal, string-typed mirror of
    /// <c>Contigo.Chat.Application.Pack.PackItem</c> — see <see cref="AnswerFromPack"/>'s own doc
    /// comment for why a structural (JSON field name) match is the only contract between the two,
    /// not a shared type.</summary>
    private sealed record FixturePackItem(
        string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet,
        IReadOnlyList<FixturePackValue>? Values);

    /// <summary>Mirrors <c>Contigo.Chat.Application.Pack.PackValue</c> — <see cref="Kind"/> is the
    /// enum's own name string ("Amount"/"Percentage"/"Date"/"Number"), never re-typed as the
    /// Contigo.Chat enum this project cannot reference.</summary>
    private sealed record FixturePackValue(string Key, string Value, string Kind, string? Currency);

    /// <inheritdoc/>
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
    /// </summary>
    private static IReadOnlyList<AiOcrPage> DecodePages(ReadOnlySpan<byte> content)
    {
        // Task E13/F04/US01/T01 (documents-admission): POST /api/documents now admits an image only
        // when its bytes carry a real PNG/JPEG signature, so a fixture "scanned image" is that
        // signature followed by UTF-8 page text (form-feed separated) — the signature is stripped
        // here and the remainder decoded exactly as before. A real photo (binary after the
        // signature) still fails strict UTF-8 decoding and gets the honest placeholder below.
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
    /// <c>Contigo.Documents.Contracts.Domain.Embedding.VectorDimensions</c> (ADR-004: "dimension
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
