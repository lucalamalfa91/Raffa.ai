namespace Raffa.Chat.Application.Capabilities;

/// <summary>
/// The fixed planner intents this task's routing table maps to catalog keys (ADR-024 "planner
/// (fixed intents)"; task E13/F08/US01/T01 coding objective's own routing table; R-SYS-02).
/// </summary>
public enum CapabilityIntentKind
{
    /// <summary>benchmark / compare / competitor / "in linea" → <see cref="CapabilityCatalog.QuoteCheckKey"/>
    /// (+ <see cref="CapabilityCatalog.ContractDetailKey"/> when a contract is already known).</summary>
    Benchmark,

    /// <summary>A named supplier with no validated contract → <see cref="CapabilityCatalog.DocumentsKey"/>
    /// (R-SYS-02 "unknown supplier → Upload in Documents").</summary>
    UnknownSupplier,

    /// <summary>deadlines / notice / renew → <see cref="CapabilityCatalog.RenewalsKey"/>.</summary>
    Deadline,

    /// <summary>saving / risparmio → <see cref="CapabilityCatalog.SavingsKey"/>.</summary>
    Savings,

    /// <summary>how-to → the specific <see cref="CapabilityIntent.CapabilityKey"/> named capability.</summary>
    HowTo,

    /// <summary>"what can you do" / "come faccio a…" → the module list (R-SYS-02): renewals,
    /// portfolio, quote check, in that order.</summary>
    CapabilityList,
}

/// <summary>
/// One detected intent for <see cref="CapabilityRouting.ResolveActions"/> — a <see cref="Kind"/>
/// plus, for <see cref="CapabilityIntentKind.HowTo"/> only, which <see cref="Capability.Key"/> the
/// question named. A later task's planner is the real producer of these; the named static members
/// below are this task's own stand-in until that planner exists (see
/// <see cref="CapabilityRouting"/>'s own doc comment).
/// </summary>
public sealed record CapabilityIntent(CapabilityIntentKind Kind, string? CapabilityKey = null)
{
    public static readonly CapabilityIntent Benchmark = new(CapabilityIntentKind.Benchmark);
    public static readonly CapabilityIntent UnknownSupplier = new(CapabilityIntentKind.UnknownSupplier);
    public static readonly CapabilityIntent Deadline = new(CapabilityIntentKind.Deadline);
    public static readonly CapabilityIntent Savings = new(CapabilityIntentKind.Savings);
    public static readonly CapabilityIntent CapabilityList = new(CapabilityIntentKind.CapabilityList);

    public static CapabilityIntent HowTo(string capabilityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityKey);
        return new CapabilityIntent(CapabilityIntentKind.HowTo, capabilityKey);
    }
}

/// <summary>
/// Turns a set of <see cref="CapabilityIntent"/>s into the <see cref="CopilotAction"/>s an Ask
/// reply offers (task E13/F08/US01/T01 coding objective: "CapabilityRouting: intent → capability
/// keys ..., and ResolveActions(intents, RoutingContext {...}) → CopilotAction(...) list with
/// hrefs built only from catalog patterns and known ids"). Stateless — like
/// <c>AskRaffaQueryRouter</c> (no dependency on anything, yet still an injected instance rather
/// than a static class, registered `AddScoped` by <c>AddChatModule</c> for the same "uniform
/// per-request/job lifetime across the module" reason that file's own doc comment gives) — this
/// class never queries a database itself; every fact it needs (validated-contract count, caller
/// role, known object ids) arrives already resolved on <see cref="RoutingContext"/>.
///
/// <para>
/// <b>Hrefs, only from catalog patterns and known ids (R-SYS-02/AC-2)</b>: every produced
/// <see cref="CopilotAction.Href"/> is either a <see cref="Capability.RoutePattern"/> verbatim, that
/// pattern's own placeholder substituted with a <see cref="RoutingContext"/> id
/// (<see cref="CapabilityCatalog.ContractDetailKey"/>'s `/contracts/{contractId}`,
/// <see cref="CapabilityCatalog.DocumentsReviewKey"/>'s `/documents?review={documentId}`), or that
/// pattern plus an id-scoped suffix this class itself knows about
/// (<see cref="CapabilityCatalog.RenewalsKey"/>'s `?select={contractId}`,
/// <see cref="CapabilityCatalog.QuoteCheckKey"/>'s `/{quoteId}` — AC-2's own four named href
/// shapes). <see cref="ForKey"/> throws rather than ever return an href that still contains an
/// unresolved `{` — a parameterized capability requested without its matching id is a caller
/// contract violation, not a broken link a reply should ever show.
/// </para>
///
/// <para>
/// <b>Availability replacement (R-SYS-04)</b>: a capability whose <see cref="CapabilityAvailability"/>
/// is <see cref="CapabilityAvailability.NeedsValidatedContract"/> becomes the Documents upload
/// action, labelled "Upload a contract", the moment
/// <see cref="RoutingContext.ValidatedContractCount"/> is zero — regardless of which intent asked
/// for it. Two or three greyed capabilities resolved together (e.g. the capability-list intent with
/// zero validated contracts) collapse to that one upload action via <see cref="CopilotAction"/>'s
/// own record equality, not three identical buttons.
/// </para>
///
/// <para>
/// <b>Role gate</b>: an admin-gated capability (<see cref="CapabilityCatalog.WorkspaceMembersKey"/>)
/// resolves to no action at all for a <see cref="CapabilityCallerRole.Standard"/> caller — silently
/// omitted, not surfaced as a broken/forbidden link.
/// </para>
/// </summary>
public sealed class CapabilityRouting
{
    /// <summary>ADR-024 empty-state copy (`raffa-v2/markup.html`, Portfolio's `kbOff` block):
    /// "The portfolio lights up from validated contracts. Upload one to start." (R-SYS-04). A
    /// constant, not embedded only in a label, so a later task's reply composer can splice this
    /// exact sentence next to the replacement <see cref="CopilotAction"/> without retyping the
    /// prototype's copy.</summary>
    public const string ValidatedContractsEmptyStateCopy =
        "The portfolio lights up from validated contracts. Upload one to start.";

    private const string UploadActionLabel = "Upload a contract";
    private const string UploadInDocumentsLabel = "Upload in Documents";

    public IReadOnlyList<CopilotAction> ResolveActions(
        IReadOnlyList<CapabilityIntent> intents, RoutingContext context)
    {
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(context);

        var actions = new List<CopilotAction>();
        foreach (var intent in intents)
        {
            actions.AddRange(ResolveOne(intent, context));
        }

        // Record equality: three identically-replaced "Upload a contract" actions (e.g. the
        // capability-list intent when every one of renewals/portfolio/quote-check is greyed)
        // collapse to one — see the type doc comment's "Availability replacement" note.
        return actions.Distinct().ToList();
    }

    private static IEnumerable<CopilotAction> ResolveOne(CapabilityIntent intent, RoutingContext context)
    {
        IEnumerable<CopilotAction?> raw = intent.Kind switch
        {
            CapabilityIntentKind.Benchmark => ResolveBenchmark(context),
            CapabilityIntentKind.UnknownSupplier => [UploadInDocuments()],
            CapabilityIntentKind.Deadline => [ForKey(CapabilityCatalog.RenewalsKey, context)],
            CapabilityIntentKind.Savings => [ForKey(CapabilityCatalog.SavingsKey, context)],
            CapabilityIntentKind.HowTo => intent.CapabilityKey is null
                ? []
                : [ForKey(intent.CapabilityKey, context)],
            CapabilityIntentKind.CapabilityList =>
            [
                ForKey(CapabilityCatalog.RenewalsKey, context),
                ForKey(CapabilityCatalog.PortfolioKey, context),
                ForKey(CapabilityCatalog.QuoteCheckKey, context),
            ],
            _ => [],
        };

        return raw.OfType<CopilotAction>();
    }

    private static IEnumerable<CopilotAction?> ResolveBenchmark(RoutingContext context)
    {
        yield return ForKey(CapabilityCatalog.QuoteCheckKey, context);

        // "+ contract-360 when validated" (coding objective): a named, already-validated supplier
        // means the RoutingContext already carries that contract's id — deep-link straight to it
        // alongside the Quote check action.
        if (context.ContractId is not null)
        {
            yield return ForKey(CapabilityCatalog.ContractDetailKey, context);
        }
    }

    /// <summary><see langword="null"/> when <paramref name="key"/>'s capability gates on a role the
    /// caller does not have (see the type doc comment's "Role gate" note) — never for any other
    /// reason; an unknown key or an unresolvable placeholder both throw (caller contract
    /// violations, not routing outcomes).</summary>
    private static CopilotAction? ForKey(string key, RoutingContext context)
    {
        var capability = CapabilityCatalog.Find(key)
            ?? throw new InvalidOperationException($"Unknown capability key '{key}'.");

        if (capability.RoleGate == CapabilityRoleGate.Admin && context.Role != CapabilityCallerRole.Admin)
        {
            return null;
        }

        if (capability.Availability == CapabilityAvailability.NeedsValidatedContract
            && context.ValidatedContractCount <= 0)
        {
            return UploadReplacement();
        }

        var href = BuildHref(capability, context);
        if (href.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Capability '{key}' needs an object id in {nameof(RoutingContext)} to build a " +
                $"complete href (pattern '{capability.RoutePattern}') — resolve the id before " +
                "calling ResolveActions for this capability, or route to a different capability.");
        }

        return new CopilotAction(DefaultLabel(capability), href, CopilotActionKind.Navigate);
    }

    /// <summary>`app.jsx`'s own capabilities-branch button labels ("Renewals →", "Portfolio →",
    /// "Quote check →" — `raffa-v2/app.jsx`'s capability-list reply actions, verbatim per
    /// ADR-024) for the three module-list targets; every other capability gets this task's own
    /// generic "Open {title}" label (no ADR/spec pins a literal string for the rest).</summary>
    private static string DefaultLabel(Capability capability) => capability.Key switch
    {
        CapabilityCatalog.RenewalsKey => "Renewals →",
        CapabilityCatalog.PortfolioKey => "Portfolio →",
        CapabilityCatalog.QuoteCheckKey => "Quote check →",
        _ => $"Open {capability.Title}",
    };

    /// <summary>AC-2's four named href shapes: `/contracts/{id}`, `/quotes/{id}`,
    /// `/documents?review={id}`, `/renewals?select={id}` — the first two substitute the
    /// capability's own placeholder pattern; the last two append to a base list pattern that
    /// carries no placeholder of its own.</summary>
    private static string BuildHref(Capability capability, RoutingContext context)
    {
        if (capability.Key == CapabilityCatalog.ContractDetailKey && context.ContractId is { } contractId)
        {
            return capability.RoutePattern.Replace("{contractId}", contractId.ToString(), StringComparison.Ordinal);
        }

        if (capability.Key == CapabilityCatalog.DocumentsReviewKey && context.DocumentId is { } documentId)
        {
            return capability.RoutePattern.Replace("{documentId}", documentId.ToString(), StringComparison.Ordinal);
        }

        if (capability.Key == CapabilityCatalog.RenewalsKey && context.ContractId is { } renewalContractId)
        {
            return $"{capability.RoutePattern}?select={renewalContractId}";
        }

        if (capability.Key == CapabilityCatalog.QuoteCheckKey && context.QuoteId is { } quoteId)
        {
            return $"{capability.RoutePattern}/{quoteId}";
        }

        return capability.RoutePattern;
    }

    /// <summary>R-SYS-02 "unknown supplier → Upload in Documents" — a distinct label from
    /// <see cref="UploadReplacement"/>'s R-SYS-04 copy, even though both target the same
    /// <see cref="CapabilityCatalog.DocumentsKey"/> href, because the two requirements name two
    /// different literal strings for two different situations.</summary>
    private static CopilotAction UploadInDocuments() =>
        new(UploadInDocumentsLabel, DocumentsRoutePattern(), CopilotActionKind.Upload);

    private static CopilotAction UploadReplacement() =>
        new(UploadActionLabel, DocumentsRoutePattern(), CopilotActionKind.Upload);

    private static string DocumentsRoutePattern() =>
        CapabilityCatalog.Find(CapabilityCatalog.DocumentsKey)?.RoutePattern
            ?? throw new InvalidOperationException("Catalog is missing the 'documents' capability.");
}
