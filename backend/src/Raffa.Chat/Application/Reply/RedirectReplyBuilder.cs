using Raffa.Chat.Application.Capabilities;

namespace Raffa.Chat.Application.Reply;

/// <summary>
/// Deterministic, zero-retrieval replies for every <c>Gate.DomainGate</c> label that never reaches
/// the planner/pack/model (task E13/F06/US01/T01, ask-engine; `inputs/requirements.md` R-ASK-02).
/// No <c>Raffa.AiGateway</c> call happens for any of these — <see cref="ReplyProvenance.NoModelCall"/>
/// on every result is the proof, not just a claim.
/// </summary>
public static class RedirectReplyBuilder
{
    /// <summary>
    /// <see cref="Domain.GateLabel.Greeting"/>/<see cref="Domain.GateLabel.OffDomain"/> — warm
    /// decline + portfolio hook (R-ASK-02: "naming a real contract or saving of this tenant, or an
    /// upload invite if empty").
    /// </summary>
    /// <param name="hookSentence">A caller-composed sentence naming a real contract/saving of this
    /// tenant (e.g. "your Salesforce renewal closes in 40 days" or "the largest identified saving
    /// is on your AWS contract"), or <see langword="null"/> when the tenant has no validated
    /// contracts to hook onto — the upload invite is used instead.</param>
    /// <param name="actions">The hook's own action when <paramref name="hookSentence"/> is
    /// non-null (e.g. open that contract), or the Documents upload action when it is null —
    /// resolved by the composition root, never authored here.</param>
    public static CopilotReply GreetingOrOffDomain(string? hookSentence, IReadOnlyList<CopilotAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var markdown = hookSentence is not null
            ? $"Hi! I'm Raffa, your savings copilot — I only answer from your validated " +
              $"contracts, never the web. For example, {hookSentence}. Ask me about dates, spend, " +
              "notice periods or clauses whenever you're ready."
            : "Hi! I'm Raffa, your savings copilot. Nothing is validated yet, so I have nothing to " +
              "answer from — upload a contract in Documents and I'll start finding you savings.";

        return new CopilotReply(ReplyKind.Redirect, markdown, [], actions, ReplyProvenance.NoModelCall([]), []);
    }

    /// <summary>
    /// <see cref="Domain.GateLabel.NeedsDocument"/> — a named supplier this tenant has not
    /// uploaded/validated (R-ASK-03; prototype oracle: "No {Who} contract has been uploaded and
    /// validated...").
    /// </summary>
    /// <param name="namedSupplier">The supplier name the question named
    /// (<c>Gate.DomainGateResult.NamedSupplier</c>).</param>
    /// <param name="actions">The Documents upload action (and, when the question also looked like
    /// a benchmark ask, the Quote check action) — resolved by the composition root.</param>
    public static CopilotReply NeedsDocument(string namedSupplier, IReadOnlyList<CopilotAction> actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namedSupplier);
        ArgumentNullException.ThrowIfNull(actions);

        var markdown =
            $"No {namedSupplier} contract has been uploaded and validated, so I have nothing " +
            "reliable to answer from. Upload it in Documents, or use Quote check if you only hold " +
            "a quote.";

        return new CopilotReply(ReplyKind.Redirect, markdown, [], actions, ReplyProvenance.NoModelCall([]), []);
    }

    /// <summary>
    /// <see cref="Domain.GateLabel.Legal"/> — refuses the legal reading, offers the commercial
    /// analogue instead (R-ASK-02 AC-3: "no legal advice; commercial analogue... + action to
    /// Contract 360").
    /// </summary>
    /// <param name="actions">The Contract 360 action (scoped to a named, resolved contract when
    /// one was named) — resolved by the composition root.</param>
    public static CopilotReply Legal(IReadOnlyList<CopilotAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        const string markdown =
            "I can't give legal advice, but I can tell you whether the relevant clause sits above " +
            "or below the market band, and point you to the contract it lives in — that's a " +
            "commercial reading, not a legal one. For anything that needs an actual legal opinion, " +
            "please talk to your own counsel.";

        return new CopilotReply(ReplyKind.Refusal, markdown, [], actions, ReplyProvenance.NoModelCall([]), []);
    }

    /// <summary>
    /// <see cref="Domain.GateLabel.Capability"/> — a real, helpful answer from the static
    /// capability catalog (R-ASK-02: "answer from the capability catalog"), never a decline —
    /// kind stays <see cref="ReplyKind.Answer"/> even though no model call was made.
    /// </summary>
    /// <param name="markdown">Deterministic catalog-derived copy (module list / how-to steps).</param>
    /// <param name="citations">Feature citation cards (<see cref="Pack.PackCorpus.Raffa"/>) for
    /// the capabilities named in <paramref name="markdown"/>.</param>
    /// <param name="actions">Real catalog routes for the capabilities named.</param>
    public static CopilotReply Capability(
        string markdown, IReadOnlyList<ReplyCitation> citations, IReadOnlyList<CopilotAction> actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(actions);

        var sources = citations.Select(c => c.Corpus).Distinct(StringComparer.Ordinal).ToList();
        return new CopilotReply(ReplyKind.Answer, markdown, citations, actions, ReplyProvenance.NoModelCall(sources), []);
    }
}
