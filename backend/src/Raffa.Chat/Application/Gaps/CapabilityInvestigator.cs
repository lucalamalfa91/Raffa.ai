using System.Text.Json;
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
/// <c>unusable</c>, <c>known-gap</c>, <c>gap</c>) — never model text.
/// </summary>
public sealed record GapInvestigation(
    GapVerdict Verdict,
    string Outcome,
    CapabilityGap? Gap,
    IReadOnlyList<string> AlternativeQuestions,
    string? Failure,
    AiCallMetadata? Metadata)
{
    public static GapInvestigation NotFound(string outcome, string? failure = null, AiCallMetadata? metadata = null) =>
        new(GapVerdict.None, outcome, null, [], failure, metadata);
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
/// <para><b>Jev decides the verdict; Foundry only ever writes.</b> When the Jev classify-role
/// pilot is on (<see cref="AiGatewayJevOptions.Enabled"/>), the actual classification —
/// verdict/knownGapKey/nearestCapabilityKey — is asked of Jev as real "choice" decisions
/// (<see cref="JevVerdictClient"/>), never the LLM. The `analyst`-role Foundry call
/// (<see cref="IAiGateway.AnalyzeAsync"/>) only ever runs afterwards, and only for a <c>gap</c>
/// verdict, to write the free-text feature description Jev's choice/noul/score primitives cannot
/// produce (<c>Raffa.AiGateway.Configuration.AiGatewayJevOptions</c>'s own doc comment) — Foundry's
/// own verdict/confidence on that call are discarded, Jev's stand. A Jev failure of any kind falls
/// back to the pre-existing Foundry-only path unchanged, so this pilot can never leave a turn worse
/// off than before it existed.</para>
/// </summary>
public sealed class CapabilityInvestigator(
    IAiGateway aiGateway, GapInvestigationOptions options, AiGatewayJevOptions jevOptions, JevVerdictClient jevVerdictClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        if (jevOptions.Enabled)
        {
            JevVerdictAnswer jevAnswer;
            try
            {
                var jevResult = await jevVerdictClient
                    .DecideAsync(
                        input,
                        BuildVerdictCriteria(),
                        BuildKeyedCriteria(knownGaps, g => g.Key, g => g.Operation),
                        BuildKeyedCriteria(capabilities, c => c.Key, c => c.Title),
                        timeout.Token)
                    .ConfigureAwait(false);

                if (jevResult.IsFailure)
                {
                    // Jev itself is unreachable/misconfigured/unparseable this call -- fall back to
                    // the pre-existing path exactly as if this pilot did not exist.
                    return await InvestigateWithFoundryAsync(input, question, knownSupplierNames, timeout.Token)
                        .ConfigureAwait(false);
                }

                jevAnswer = jevResult.Value;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return GapInvestigation.NotFound("failed", $"timed out after {options.TimeoutSeconds}s.");
            }

            return await InterpretJevAsync(jevAnswer, question, knownSupplierNames, input, timeout.Token)
                .ConfigureAwait(false);
        }

        return await InvestigateWithFoundryAsync(input, question, knownSupplierNames, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>The pre-existing (pre-Jev-pilot) path: one Foundry `analyst` call decides
    /// everything -- verdict, confidence and, for a gap, the feature description. Unchanged
    /// behaviour, used whenever the Jev pilot is off or fails.</summary>
    private async Task<GapInvestigation> InvestigateWithFoundryAsync(
        string input, string question, IReadOnlyCollection<string> knownSupplierNames, CancellationToken cancellationToken)
    {
        AiAnalysisResult analysis;
        try
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

            analysis = result.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GapInvestigation.NotFound("failed", $"timed out after {options.TimeoutSeconds}s.");
        }

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

        return Interpret(payload, question, knownSupplierNames, analysis.Metadata);
    }

    /// <summary>Interprets a Jev-decided verdict. <c>question</c>/<c>supported</c>/<c>known-gap</c>
    /// never touch Foundry at all; <c>gap</c> makes exactly one Foundry call, only to write the
    /// feature description -- see this type's own doc comment.</summary>
    private async Task<GapInvestigation> InterpretJevAsync(
        JevVerdictAnswer jevAnswer,
        string question,
        IReadOnlyCollection<string> knownSupplierNames,
        string input,
        CancellationToken cancellationToken)
    {
        var verdict = jevAnswer.Verdict;

        if (verdict is CapabilityInvestigatorAgent.VerdictQuestion or CapabilityInvestigatorAgent.VerdictSupported)
        {
            return new GapInvestigation(GapVerdict.Supported, verdict, null, [], null, jevAnswer.Metadata);
        }

        if (verdict is not (CapabilityInvestigatorAgent.VerdictKnownGap or CapabilityInvestigatorAgent.VerdictGap))
        {
            return GapInvestigation.NotFound("failed", $"Jev named an unrecognized verdict '{verdict}'.", jevAnswer.Metadata);
        }

        var confidence = BucketConfidence(jevAnswer.Confidence);
        if (!MeetsThreshold(confidence))
        {
            return GapInvestigation.NotFound("low-confidence", null, jevAnswer.Metadata);
        }

        if (verdict == CapabilityInvestigatorAgent.VerdictKnownGap)
        {
            var known = CapabilityGapCatalog.Find((jevAnswer.KnownGapKey ?? string.Empty).Trim());
            return known is null || known.IsVetoed(question)
                ? GapInvestigation.NotFound(
                    "unusable", $"known-gap key '{jevAnswer.KnownGapKey}' is not in the catalog or is vetoed.", jevAnswer.Metadata)
                : new GapInvestigation(GapVerdict.KnownGap, "known-gap", known, [], null, jevAnswer.Metadata);
        }

        // verdict == gap: write the feature description on Foundry -- the one thing Jev's
        // choice/noul/score primitives cannot produce. Jev's own verdict/confidence already
        // decided this is a gap; this call's own verdict/confidence/knownGapKey/
        // nearestCapabilityKey fields are ignored on purpose (Interpret's Foundry-only sibling
        // reads them; this path reads only Feature/AlternativeQuestions from the same payload).
        AiAnalysisResult featureAnalysis;
        try
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
                return GapInvestigation.NotFound("failed", result.Error, jevAnswer.Metadata);
            }

            featureAnalysis = result.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GapInvestigation.NotFound("failed", "timed out writing the gap description.", jevAnswer.Metadata);
        }

        InvestigatorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InvestigatorPayload>(featureAnalysis.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return GapInvestigation.NotFound(
                "failed", $"gap-description payload was not valid JSON — {ex.Message}", featureAnalysis.Metadata);
        }

        if (payload is null)
        {
            return GapInvestigation.NotFound("failed", "gap-description payload parsed to null.", featureAnalysis.Metadata);
        }

        return BuildGapInvestigation(
            payload.Feature, jevAnswer.NearestCapabilityKey, confidence, payload.AlternativeQuestions,
            question, knownSupplierNames, featureAnalysis.Metadata);
    }

    /// <summary>Buckets Jev's raw <c>[0,1]</c> calibrated confidence into the existing
    /// high/medium/low vocabulary <see cref="MeetsThreshold"/> already gates on -- see
    /// <see cref="GapInvestigationOptions.JevHighConfidenceThreshold"/>'s own doc comment for why
    /// these thresholds are a starting point, not a measured calibration.</summary>
    private string BucketConfidence(double confidence) =>
        confidence >= options.JevHighConfidenceThreshold ? CapabilityInvestigatorAgent.ConfidenceHigh
        : confidence >= options.JevMediumConfidenceThreshold ? CapabilityInvestigatorAgent.ConfidenceMedium
        : CapabilityInvestigatorAgent.ConfidenceLow;

    private static IReadOnlyDictionary<string, string> BuildVerdictCriteria() => new Dictionary<string, string>
    {
        [CapabilityInvestigatorAgent.VerdictQuestion] =
            "An ordinary question, analysis, ranking, comparison or explanation Ask can already give in the chat or a screen already shows.",
        [CapabilityInvestigatorAgent.VerdictSupported] =
            "An operation a screen or an Ask ability already performs (upload a contract, review a field, check a quote, invite a teammate).",
        [CapabilityInvestigatorAgent.VerdictKnownGap] =
            "One of the fixed known-gap operations already in the catalog.",
        [CapabilityInvestigatorAgent.VerdictGap] =
            "An operation or deliverable nothing in Raffa performs today -- a new feature the product team could build.",
    };

    private static IReadOnlyDictionary<string, string> BuildKeyedCriteria<T>(
        IReadOnlyList<T> items, Func<T, string> key, Func<T, string> description) =>
        items.ToDictionary(key, description);

    private GapInvestigation Interpret(
        InvestigatorPayload payload, string question, IReadOnlyCollection<string> knownSupplierNames, AiCallMetadata metadata)
    {
        var verdict = (payload.Verdict ?? string.Empty).Trim();

        if (verdict is CapabilityInvestigatorAgent.VerdictQuestion or CapabilityInvestigatorAgent.VerdictSupported)
        {
            return new GapInvestigation(GapVerdict.Supported, verdict, null, [], null, metadata);
        }

        if (verdict is not (CapabilityInvestigatorAgent.VerdictKnownGap or CapabilityInvestigatorAgent.VerdictGap))
        {
            return GapInvestigation.NotFound("failed", $"unknown verdict '{verdict}'.", metadata);
        }

        var confidence = (payload.Confidence ?? string.Empty).Trim();
        if (!MeetsThreshold(confidence))
        {
            return GapInvestigation.NotFound("low-confidence", null, metadata);
        }

        if (verdict == CapabilityInvestigatorAgent.VerdictKnownGap)
        {
            // The model names a catalog entry; the catalog's own veto still applies ("when must we
            // send the notice?" is a deadline question whoever recognised the verb).
            var known = CapabilityGapCatalog.Find((payload.KnownGapKey ?? string.Empty).Trim());
            return known is null || known.IsVetoed(question)
                ? GapInvestigation.NotFound("unusable", $"known-gap key '{payload.KnownGapKey}' is not in the catalog or is vetoed.", metadata)
                : new GapInvestigation(GapVerdict.KnownGap, "known-gap", known, [], null, metadata);
        }

        return BuildGapInvestigation(
            payload.Feature, payload.NearestCapabilityKey, confidence, payload.AlternativeQuestions,
            question, knownSupplierNames, metadata);
    }

    /// <summary>Shared "gap" construction: cleans the feature texts, resolves the nearest
    /// capability key and the alternative questions, and builds the discovered
    /// <see cref="CapabilityGap"/>. Shared by <see cref="Interpret"/> (the Foundry-only path,
    /// which reads every field from one payload) and <see cref="InterpretJevAsync"/> (which reads
    /// <paramref name="nearestCapabilityKey"/>/<paramref name="confidence"/> from Jev's own
    /// verdict and only <see cref="FeaturePayload"/>/alternative questions from Foundry).</summary>
    private GapInvestigation BuildGapInvestigation(
        FeaturePayload? feature,
        string? nearestCapabilityKey,
        string confidence,
        IReadOnlyList<string>? alternativeQuestions,
        string question,
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
            return GapInvestigation.NotFound("unusable", "a feature text was empty once cleaned.", metadata);
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

        return new GapInvestigation(GapVerdict.Gap, "gap", gap, cleanedAlternativeQuestions, null, metadata);
    }

    private bool MeetsThreshold(string confidence) =>
        confidence == CapabilityInvestigatorAgent.ConfidenceHigh ||
        (confidence == CapabilityInvestigatorAgent.ConfidenceMedium &&
         !string.Equals(options.MinConfidence, CapabilityInvestigatorAgent.ConfidenceHigh, StringComparison.OrdinalIgnoreCase));

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
