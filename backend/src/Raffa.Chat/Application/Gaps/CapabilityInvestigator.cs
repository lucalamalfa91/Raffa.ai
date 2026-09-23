using System.Text.Json;
using Raffa.AiGateway;
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
/// four verdicts; a <c>gap</c> verdict becomes a discovered <see cref="CapabilityGap"/> the
/// composition root answers exactly like a catalog gap — the honest preface, the nearest real
/// alternative and the offer to propose the feature, which a person approves on GitHub before
/// anything is built.
///
/// <para><b>Fail-open, always.</b> A disabled switch, a gateway failure, a timeout, an unparseable
/// payload, a low-confidence verdict or texts that do not survive <see cref="DiscoveredGapText"/>
/// all return <see cref="GapVerdict.None"/>: the turn is answered as it was before ADR-031. The
/// investigator never answers, never retrieves and never sees tenant data beyond the supplier
/// names it is handed to scrub.</para>
/// </summary>
public sealed class CapabilityInvestigator(IAiGateway aiGateway, GapInvestigationOptions options)
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
        var input = JsonSerializer.Serialize(
            new InvestigatorInput(
                question.Trim(),
                language,
                CapabilityCatalog.All.Select(c => new CapabilityDto(c.Key, c.Title, c.Description)).ToList(),
                CapabilityInvestigatorAgent.AskAbilities,
                CapabilityGapCatalog.All.Select(g => new KnownGapDto(g.Key, g.OperationEn, g.AlternativeEn)).ToList()),
            JsonOptions);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

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
                    timeout.Token)
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

        var feature = payload.Feature;
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

        var nearestKey = (payload.NearestCapabilityKey ?? string.Empty).Trim();
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

        var alternativeQuestions = (payload.AlternativeQuestions ?? [])
            .Select(DiscoveredGapText.CleanQuestion)
            .Where(q => q is not null)
            .Select(q => q!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(DiscoveredGapText.MaxAlternativeQuestions)
            .ToList();

        return new GapInvestigation(GapVerdict.Gap, "gap", gap, alternativeQuestions, null, metadata);
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
