using System.Text.Json;
using Microsoft.Extensions.Options;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Language;

namespace Raffa.Chat.Application.Gaps;

/// <summary>What the investigator concluded about one turn.</summary>
public enum GapVerdict
{
    /// <summary>Not asked, or no usable verdict (disabled, timed out, failed, low confidence,
    /// unusable texts): the turn goes on exactly as it would have without the investigator.</summary>
    None,

    /// <summary>An ordinary question, or an operation Raffa already performs.</summary>
    Supported,

    /// <summary>One of the fixed <see cref="CapabilityGapCatalog"/> entries, recognised by the
    /// model where the regex missed the phrasing.</summary>
    KnownGap,

    /// <summary>An operation nothing in Raffa performs — a new, discovered gap.</summary>
    Gap,
}

/// <summary>
/// The investigator's result. <see cref="Gap"/> is set for <see cref="GapVerdict.KnownGap"/> (the
/// catalog entry) and <see cref="GapVerdict.Gap"/> (a <see cref="GapOrigin.Investigator"/> gap
/// built by <see cref="CapabilityGap.Discovered"/>); <see cref="Outcome"/> is the audit value
/// (<c>off</c>, <c>failed</c>, <c>question</c>, <c>supported</c>, <c>low-confidence</c>,
/// <c>unusable</c>, <c>known-gap</c>, <c>gap</c>) — never model text. <see cref="Confidence"/> is
/// the verdict's own confidence word (<c>low</c>/<c>medium</c>/<c>high</c>) when the model gave a
/// usable one, and <see cref="TimedOut"/> tells a time-budget failure apart from any other
/// <c>failed</c> (INV-05 audits it as <c>timeout</c>).
/// </summary>
public sealed record GapInvestigation(
    GapVerdict Verdict,
    string Outcome,
    CapabilityGap? Gap,
    IReadOnlyList<string> AlternativeQuestions,
    string? Failure,
    AiCallMetadata? Metadata,
    string? Confidence = null,
    bool TimedOut = false)
{
    public static GapInvestigation NotFound(
        string outcome, string? failure = null, AiCallMetadata? metadata = null, string? confidence = null, bool timedOut = false) =>
        new(GapVerdict.None, outcome, null, [], failure, metadata, confidence, timedOut);
}

/// <summary>
/// ADR-031: Raffa's own judgement on whether a turn asks for a feature it does not have. The fixed
/// <see cref="CapabilityGapCatalog"/> recognises five operations by regex (ADR-030 D1) and misses
/// every other one — "puoi scrivere un report per il CFO?" was answered as a portfolio question.
/// This agent reads the turn beside the whole capability map (<see cref="CapabilityCatalog"/>,
/// <see cref="CapabilityInvestigatorAgent.AskAbilities"/>, the known gaps) and returns one of
/// four verdicts; a <c>gap</c> verdict becomes a discovered <see cref="CapabilityGap"/>. The host
/// runs this beside the answer and, when it finds a gap, appends a separate follow-up message after
/// the answer — the honest preface, the nearest real alternative and the offer to propose the
/// feature, which a person approves on GitHub before anything is built.
///
/// <para><b>Fail-open, always.</b> A disabled switch, a gateway failure, a timeout, an unparseable
/// payload, a low-confidence verdict or texts that do not survive <see cref="DiscoveredGapText"/>
/// all return <see cref="GapVerdict.None"/>: no follow-up, and the answer never waited for it. The
/// investigator never answers, never retrieves and never sees tenant data beyond the supplier
/// names it is handed to scrub.</para>
///
/// <para><b>Jev decides the verdict; Foundry only ever writes.</b> When the Jev classify-role pilot
/// is on (<see cref="AiGatewayJevOptions.Enabled"/>), which operation the turn asks for is a real
/// classification choice put to Jev (<see cref="JevVerdictClient"/>), never the LLM: the verdict
/// (question / supported / known-gap / gap) is derived in code from the option Jev chose. The
/// `analyst`-role Foundry call (<see cref="IAiGateway.AnalyzeAsync"/>) only ever runs afterwards, and
/// only for a <c>gap</c>, to write the free-text feature description Jev's choice/noul/score
/// primitives cannot produce -- Foundry's own verdict and confidence on that call are discarded, and
/// a description Foundry could not write (it read the turn differently) leaves no follow-up. A Jev
/// failure of any kind falls back to the Foundry-only path unchanged, so this pilot can never leave
/// a turn worse off than before it existed.</para>
/// </summary>
public sealed class CapabilityInvestigator(
    IAiGateway aiGateway,
    IOptionsMonitor<GapInvestigationOptions> optionsMonitor,
    AiGatewayJevOptions jevOptions,
    JevVerdictClient jevVerdictClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A fixed options instance (unit tests, tools): never re-read.</summary>
    public CapabilityInvestigator(
        IAiGateway aiGateway,
        GapInvestigationOptions options,
        AiGatewayJevOptions jevOptions,
        JevVerdictClient jevVerdictClient)
        : this(aiGateway, StaticGapInvestigationOptions.Monitor(options), jevOptions, jevVerdictClient)
    {
    }

    /// <param name="question">The user's message, as typed.</param>
    /// <param name="knownSupplierNames">Every supplier this tenant has on file — removed from the
    /// feature texts, never sent to the model.</param>
    public async Task<GapInvestigation> InvestigateAsync(
        string question,
        IReadOnlyCollection<string> knownSupplierNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(knownSupplierNames);

        // One snapshot per investigation: a configuration change applies to the next one.
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return GapInvestigation.NotFound("off");
        }

        var language = QuestionLanguage.Detect(question);
        var capabilities = CapabilityCatalog.All.Select(c => new CapabilityDto(c.Key, c.Title, c.Description)).ToList();
        var knownGaps = CapabilityGapCatalog.All.Select(g => new KnownGapDto(g.Key, g.OperationEn, g.AlternativeEn)).ToList();
        var input = JsonSerializer.Serialize(
            new InvestigatorInput(question.Trim(), language, capabilities, CapabilityInvestigatorAgent.AskAbilities, knownGaps),
            JsonOptions);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

        try
        {
            if (jevOptions.Enabled)
            {
                var jevResult = await jevVerdictClient
                    .DecideAsync(
                        question.Trim(),
                        language,
                        BuildOperationCriteria(capabilities, knownGaps),
                        BuildNearestCapabilityCriteria(capabilities),
                        timeout.Token)
                    .ConfigureAwait(false);

                // A Jev call that cannot answer falls back to the pre-existing path exactly as if
                // this pilot did not exist.
                if (jevResult.IsSuccess)
                {
                    return await InterpretJevAsync(
                            jevResult.Value, question.Trim(), knownSupplierNames, input, options, timeout.Token)
                        .ConfigureAwait(false);
                }
            }

            return await InvestigateWithFoundryAsync(input, question, knownSupplierNames, options, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GapInvestigation.NotFound("failed", $"timed out after {options.TimeoutSeconds}s.", timedOut: true);
        }
    }

    /// <summary>The pre-Jev path: one Foundry `analyst` call decides everything -- verdict,
    /// confidence and, for a gap, the feature description. Unchanged behaviour, used whenever the Jev
    /// pilot is off or cannot answer.</summary>
    private async Task<GapInvestigation> InvestigateWithFoundryAsync(
        string input,
        string question,
        IReadOnlyCollection<string> knownSupplierNames,
        GapInvestigationOptions options,
        CancellationToken cancellationToken)
    {
        var result = await aiGateway.AnalyzeAsync(
                new AiAnalysisRequest(
                    CapabilityInvestigatorAgent.Name,
                    CapabilityInvestigatorAgent.Prompt,
                    input,
                    CapabilityInvestigatorAgent.Schema,
                    CapabilityInvestigatorAgent.Version),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return GapInvestigation.NotFound("failed", result.Error);
        }

        var analysis = result.Value;
        InvestigatorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InvestigatorPayload>(analysis.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return GapInvestigation.NotFound("failed", $"payload was not valid JSON — {ex.Message}", analysis.Metadata);
        }

        if (payload is null)
        {
            return GapInvestigation.NotFound("failed", "payload parsed to null.", analysis.Metadata);
        }

        return Interpret(payload, question, knownSupplierNames, analysis.Metadata, options);
    }

    /// <summary>Derives the verdict from the operation Jev chose. <c>question</c> and a
    /// <c>capability:</c> option never touch Foundry; a <c>known-gap:</c> option needs no free text
    /// either; <c>none</c> makes exactly one Foundry call, only to write the feature description.</summary>
    private async Task<GapInvestigation> InterpretJevAsync(
        JevVerdictAnswer jev,
        string question,
        IReadOnlyCollection<string> knownSupplierNames,
        string input,
        GapInvestigationOptions options,
        CancellationToken cancellationToken)
    {
        var bucket = BucketConfidence(jev.Confidence, options);

        if (jev.Operation == JevVerdictClient.OperationQuestion)
        {
            return new GapInvestigation(
                GapVerdict.Supported, CapabilityInvestigatorAgent.VerdictQuestion, null, [], null, jev.Metadata, bucket);
        }

        if (jev.Operation.StartsWith(JevVerdictClient.CapabilityPrefix, StringComparison.Ordinal))
        {
            return new GapInvestigation(
                GapVerdict.Supported, CapabilityInvestigatorAgent.VerdictSupported, null, [], null, jev.Metadata, bucket);
        }

        var isKnownGap = jev.Operation.StartsWith(JevVerdictClient.KnownGapPrefix, StringComparison.Ordinal);
        if (!isKnownGap && jev.Operation != JevVerdictClient.OperationNone)
        {
            return GapInvestigation.NotFound("failed", $"Jev named an unrecognized operation '{jev.Operation}'.", jev.Metadata);
        }

        // A known gap or a new gap triggers a follow-up message, so it must clear the confidence bar
        // -- and the two option orders must have agreed on it.
        if (!MeetsThreshold(bucket, options))
        {
            return GapInvestigation.NotFound("low-confidence", null, jev.Metadata, bucket);
        }

        if (isKnownGap)
        {
            var key = jev.Operation[JevVerdictClient.KnownGapPrefix.Length..];
            var known = CapabilityGapCatalog.Find(key);
            return known is null || known.IsVetoed(question)
                ? GapInvestigation.NotFound(
                    "unusable", $"known-gap key '{key}' is not in the catalog or is vetoed.", jev.Metadata, bucket)
                : new GapInvestigation(GapVerdict.KnownGap, "known-gap", known, [], null, jev.Metadata, bucket);
        }

        // Operation "none": a gap. Foundry writes the feature description -- the one thing Jev's
        // primitives cannot produce. Its own verdict, confidence and keys are ignored on purpose.
        var result = await aiGateway.AnalyzeAsync(
                new AiAnalysisRequest(
                    CapabilityInvestigatorAgent.Name,
                    CapabilityInvestigatorAgent.Prompt,
                    input,
                    CapabilityInvestigatorAgent.Schema,
                    CapabilityInvestigatorAgent.Version),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return GapInvestigation.NotFound("failed", result.Error, jev.Metadata, bucket);
        }

        var featureAnalysis = result.Value;
        InvestigatorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InvestigatorPayload>(featureAnalysis.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return GapInvestigation.NotFound(
                "failed", $"gap-description payload was not valid JSON — {ex.Message}", featureAnalysis.Metadata, bucket);
        }

        if (payload is null)
        {
            return GapInvestigation.NotFound(
                "failed", "gap-description payload parsed to null.", featureAnalysis.Metadata, bucket);
        }

        return BuildGapInvestigation(
            payload.Feature,
            jev.NearestCapabilityKey,
            bucket,
            payload.AlternativeQuestions,
            knownSupplierNames,
            featureAnalysis.Metadata);
    }

    /// <summary>The operation options in the order presented to Jev: the ordinary question first,
    /// then what Raffa already does, then what it knows it does not do, then <c>none</c>. (The
    /// reversed copy asked beside it removes the first-option lean.)</summary>
    private static IReadOnlyDictionary<string, string> BuildOperationCriteria(
        IReadOnlyList<CapabilityDto> capabilities, IReadOnlyList<KnownGapDto> knownGaps)
    {
        var criteria = new Dictionary<string, string>
        {
            [JevVerdictClient.OperationQuestion] =
                "An ordinary question, analysis, ranking, comparison or explanation that Ask already gives in the chat: " +
                string.Join(" ", CapabilityInvestigatorAgent.AskAbilities),
        };

        foreach (var capability in capabilities)
        {
            criteria[JevVerdictClient.CapabilityPrefix + capability.Key] =
                $"Raffa already does this: {capability.Title} -- {capability.Does}";
        }

        foreach (var gap in knownGaps)
        {
            criteria[JevVerdictClient.KnownGapPrefix + gap.Key] =
                $"Raffa does not do this yet: {gap.Operation}";
        }

        criteria[JevVerdictClient.OperationNone] =
            "An operation or deliverable that none of the other options describes -- a new feature the product team could build.";
        return criteria;
    }

    private static IReadOnlyDictionary<string, string> BuildNearestCapabilityCriteria(IReadOnlyList<CapabilityDto> capabilities)
    {
        var criteria = capabilities.ToDictionary(c => c.Key, c => c.Title);
        criteria["ask"] = "An answer in the chat.";
        return criteria;
    }

    /// <summary>Buckets Jev's <c>[0,1]</c> confidence into the high/medium/low vocabulary
    /// <see cref="MeetsThreshold"/> already gates on -- starting points, see
    /// <see cref="GapInvestigationOptions.JevHighConfidenceThreshold"/>.</summary>
    private static string BucketConfidence(double confidence, GapInvestigationOptions options) =>
        confidence >= options.JevHighConfidenceThreshold ? CapabilityInvestigatorAgent.ConfidenceHigh
        : confidence >= options.JevMediumConfidenceThreshold ? CapabilityInvestigatorAgent.ConfidenceMedium
        : CapabilityInvestigatorAgent.ConfidenceLow;

    private static GapInvestigation Interpret(
        InvestigatorPayload payload,
        string question,
        IReadOnlyCollection<string> knownSupplierNames,
        AiCallMetadata metadata,
        GapInvestigationOptions options)
    {
        var verdict = (payload.Verdict ?? string.Empty).Trim();

        if (verdict is CapabilityInvestigatorAgent.VerdictQuestion or CapabilityInvestigatorAgent.VerdictSupported)
        {
            return new GapInvestigation(GapVerdict.Supported, verdict, null, [], null, metadata, KnownConfidence(payload.Confidence));
        }

        if (verdict is not (CapabilityInvestigatorAgent.VerdictKnownGap or CapabilityInvestigatorAgent.VerdictGap))
        {
            return GapInvestigation.NotFound("failed", $"unknown verdict '{verdict}'.", metadata);
        }

        var confidence = (payload.Confidence ?? string.Empty).Trim();
        if (!MeetsThreshold(confidence, options))
        {
            return GapInvestigation.NotFound("low-confidence", null, metadata, KnownConfidence(confidence));
        }

        if (verdict == CapabilityInvestigatorAgent.VerdictKnownGap)
        {
            // The model names a catalog entry; the catalog's own veto still applies ("when must we
            // send the notice?" is a deadline question whoever recognised the verb).
            var known = CapabilityGapCatalog.Find((payload.KnownGapKey ?? string.Empty).Trim());
            return known is null || known.IsVetoed(question)
                ? GapInvestigation.NotFound("unusable", $"known-gap key '{payload.KnownGapKey}' is not in the catalog or is vetoed.", metadata, confidence)
                : new GapInvestigation(GapVerdict.KnownGap, "known-gap", known, [], null, metadata, confidence);
        }

        return BuildGapInvestigation(
            payload.Feature, payload.NearestCapabilityKey, confidence, payload.AlternativeQuestions,
            knownSupplierNames, metadata);
    }

    /// <summary>Shared "gap" construction: cleans the feature texts, resolves the nearest capability
    /// key and the alternative questions, and builds the discovered <see cref="CapabilityGap"/>.
    /// Used by the Foundry-only path (every field from one payload) and the Jev path (the nearest
    /// capability and the confidence from Jev, the texts from Foundry).</summary>
    private static GapInvestigation BuildGapInvestigation(
        FeaturePayload? feature,
        string? nearestCapabilityKey,
        string confidence,
        IReadOnlyList<string>? alternativeQuestions,
        IReadOnlyCollection<string> knownSupplierNames,
        AiCallMetadata metadata)
    {
        var titleEn = DiscoveredGapText.Clean(feature?.TitleEn, knownSupplierNames, DiscoveredGapText.MaxTitleLength);
        var titleIt = DiscoveredGapText.Clean(feature?.TitleIt, knownSupplierNames, DiscoveredGapText.MaxTitleLength);
        var operationEn = DiscoveredGapText.Clean(feature?.OperationEn, knownSupplierNames, DiscoveredGapText.MaxOperationLength);
        var operationIt = DiscoveredGapText.Clean(feature?.OperationIt, knownSupplierNames, DiscoveredGapText.MaxOperationLength);
        var descriptionEn = DiscoveredGapText.Clean(feature?.DescriptionEn, knownSupplierNames, DiscoveredGapText.MaxDescriptionLength);
        var descriptionIt = DiscoveredGapText.Clean(feature?.DescriptionIt, knownSupplierNames, DiscoveredGapText.MaxDescriptionLength);

        if (titleEn is null || titleIt is null || operationEn is null || operationIt is null ||
            descriptionEn is null || descriptionIt is null)
        {
            return GapInvestigation.NotFound("unusable", "a feature text was empty once cleaned.", metadata, confidence);
        }

        var nearestKey = (nearestCapabilityKey ?? string.Empty).Trim();
        var nearest = CapabilityCatalog.Find(nearestKey) is not null ? nearestKey : null;

        var gap = CapabilityGap.Discovered(
            DiscoveredGapText.Slug(feature?.Key, titleEn),
            titleEn,
            titleIt,
            operationEn,
            operationIt,
            descriptionEn,
            descriptionIt,
            nearest,
            confidence,
            CapabilityInvestigatorAgent.Version);

        var cleanedAlternativeQuestions = (alternativeQuestions ?? [])
            .Select(DiscoveredGapText.CleanQuestion)
            .Where(q => q is not null)
            .Select(q => q!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(DiscoveredGapText.MaxAlternativeQuestions)
            .ToList();

        return new GapInvestigation(GapVerdict.Gap, "gap", gap, cleanedAlternativeQuestions, null, metadata, confidence);
    }

    private static bool MeetsThreshold(string confidence, GapInvestigationOptions options) =>
        confidence == CapabilityInvestigatorAgent.ConfidenceHigh ||
        (confidence == CapabilityInvestigatorAgent.ConfidenceMedium &&
         !string.Equals(options.MinConfidence, CapabilityInvestigatorAgent.ConfidenceHigh, StringComparison.OrdinalIgnoreCase));

    /// <summary>The model's confidence word when it is one of the three the schema allows, else
    /// <see langword="null"/> — the audit never carries free model text.</summary>
    private static string? KnownConfidence(string? confidence)
    {
        var trimmed = (confidence ?? string.Empty).Trim();
        return trimmed is CapabilityInvestigatorAgent.ConfidenceHigh
            or CapabilityInvestigatorAgent.ConfidenceMedium
            or CapabilityInvestigatorAgent.ConfidenceLow
                ? trimmed
                : null;
    }

    // ----- wire shapes (camelCase JSON; the fixture gateway reads "question"/"language") -----

    private sealed record CapabilityDto(string Key, string Title, string Does);

    private sealed record KnownGapDto(string Key, string Operation, string InsteadRaffaOffers);

    private sealed record InvestigatorInput(
        string Question,
        string Language,
        IReadOnlyList<CapabilityDto> Capabilities,
        IReadOnlyList<string> AskAbilities,
        IReadOnlyList<KnownGapDto> KnownGaps);

    private sealed record FeaturePayload(
        string? Key,
        string? TitleEn,
        string? TitleIt,
        string? OperationEn,
        string? OperationIt,
        string? DescriptionEn,
        string? DescriptionIt);

    private sealed record InvestigatorPayload(
        string? Rationale,
        string? Verdict,
        string? Confidence,
        string? KnownGapKey,
        string? NearestCapabilityKey,
        FeaturePayload? Feature,
        IReadOnlyList<string>? AlternativeQuestions);
}
