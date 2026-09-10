namespace Raffa.AiGateway.Configuration;

/// <summary>
/// OCR-specific gateway policy (ADR-017), added by task E02/F01/US02/T02 (hybrid-ocr). Document
/// Intelligence bills per page, so a runaway upload (a corrupt file the provider tries to page
/// through forever, or a genuinely huge scan) must not silently blow the `demo` bill. ADR-017
/// "Implications for the decomposition": "A configured safety budget (max pages per job / per
/// tenant) is allowed... over-budget jobs fail visibly (failed status), they are not silently
/// truncated" — <see cref="MaxPagesPerDocument"/> is that budget.
///
/// Enforced once, inside <see cref="IAiGateway.OcrAsync"/> itself (today:
/// <see cref="Fixtures.FixtureAiGateway"/>), the same "single choke point" reasoning
/// <see cref="AiGatewayComplianceOptions"/>'s own doc comment already uses for no-training — every
/// current and future caller (today: the Documents/Contracts hybrid pre-pass) is protected without
/// having to remember to check a budget itself, and a caller can never work around the cap by
/// calling <see cref="IAiGateway.OcrAsync"/> with a hand-sliced subset of pages instead (that would
/// silently re-introduce the "2-page cap" ADR-017 forbids) — over budget means the whole call
/// fails, in full.
/// </summary>
public sealed class AiGatewayOcrOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "AiGateway:Ocr";

    /// <summary>
    /// Hard cap on pages one <see cref="IAiGateway.OcrAsync"/> call may process. ADR-017 fixes the
    /// *mechanism* (a configured budget that fails visibly, never truncates) but not the number;
    /// 300 is this task's own starting default — generous enough for a real MSA plus its order
    /// forms/schedules bundled into one upload (spec §17.1's "100 contracts" bar is about
    /// portfolio breadth, not single-document length) while still catching a pathological upload.
    /// A config value always overrides this default; nothing here is hard-coded into gateway
    /// *logic*, only into this options object's own default (mirrors
    /// <see cref="AiGatewayModelOptions"/>'s own property-initializer pattern).
    /// </summary>
    public int MaxPagesPerDocument { get; init; } = 300;

    /// <summary>
    /// Total time <see cref="Foundry.FoundryOcrClient"/> waits for a Document Intelligence analysis
    /// to reach a terminal status. Read on a multi-page scan routinely takes tens of seconds; the
    /// budget must fit inside the synchronous upload request (Container Apps ingress ~240 s) together
    /// with classification and extraction.
    /// </summary>
    public int PollTimeoutSeconds { get; init; } = 120;

    /// <summary>First poll interval when the service sends no <c>Retry-After</c>; doubles per poll
    /// up to <see cref="MaxPollIntervalMilliseconds"/>.</summary>
    public int InitialPollIntervalMilliseconds { get; init; } = 1_000;

    /// <summary>Ceiling on the poll interval.</summary>
    public int MaxPollIntervalMilliseconds { get; init; } = 5_000;
}
