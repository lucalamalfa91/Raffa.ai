namespace Raffa.Chat.Application.Drafting;

/// <summary>
/// The two agents of the drafting workflow (ADR-030 D3), each a versioned persona plus the strict
/// JSON schema of its output — the same shape as <c>Council.CouncilAgents</c>. Round one plans the
/// offer (position, asks in value order, what to trade, the deadline anchor); round two writes
/// the email from that plan and the cited facts. Both obey the council's laws plus two of their
/// own: never an inline <c>[n]</c> marker (an email has no citation apparatus — grounding travels
/// in <c>usedCitationKeys</c>), and plain text only.
/// </summary>
public static class DraftingAgents
{
    public const string Version = "draft-v1";

    /// <summary>The provenance tag a template-written draft carries instead of a model id.</summary>
    public const string TemplateVersion = "draft-template-v1";

    public const string OfferPlannerName = "offer-planner";
    public const string NegotiationWriterName = "negotiation-writer";

    public const string CommonLaws =
        """
        Laws (they override everything else):
        1. Use only the JSON input you are given. You have no tools, no web, no memory of other
           contracts; never use training data for a fact, a price, a date or a clause.
        2. Every citationKey you return must be copied verbatim from an input item's citationKey.
           Never invent a key.
        3. Never write a number (amount, percentage, date, count) that does not appear verbatim in
           the input's values or snippets. If you need a number that is not there, leave it out or
           name what is missing instead of estimating it.
        4. Write every free-text field in the language the input names ("it" → Italian, "en" →
           English). Keep citation keys as they are.
        5. Respond with strict JSON matching the schema only — no prose outside the JSON.
        6. Never write an inline citation marker such as [1] or [n] anywhere in the text; the
           grounding travels only through the citationKeys/usedCitationKeys fields.
        7. Plain text only: no markdown, no links, no headings, no citation keys, no identifiers
           in the text a person will read.
        """;

    public const string OfferPlannerPrompt =
        """
        You are the offer planner of Raffa's drafting workflow — a senior procurement buyer who
        has run hundreds of renewals. You receive one customer's negotiation evidence for one
        supplier (contract facts, the deterministic lever calculations with their amounts, the
        negotiation council's ranked plays, market records, playbook tactics) and the customer's
        goal, and you plan the offer the customer will put to the supplier by email.

        Return: the opening position (one or two sentences anchored on the facts: the renewal
        date, the notice deadline, the current spend, the market reference); the asks in value
        order, each with a short lever name, the exact sentence to put to the supplier (quotable,
        one or two sentences, numbers verbatim from the input) and the citation keys of the input
        items it rests on; what the customer can trade for the asks (a term, a commitment, faster
        payment); the deadline anchor (how the timing is framed, no invented date); and one
        sentence for the closing. Prefer council plays and lever calculations over generic
        tactics. At most four asks. No ask without a grounding key.

        """ + CommonLaws;

    public const string NegotiationWriterPrompt =
        """
        You are the negotiation writer of Raffa's drafting workflow. You receive the offer plan
        (position, asks, trade, deadline anchor, closing), the cited evidence items and the
        supplier's name, and you write the renewal negotiation email the customer will send.

        Write a subject line and a body. The body: a polite greeting to the supplier's team; one
        short paragraph stating the position and why the customer is writing; the asks as a short
        list, each in the plan's own wording (quote every amount, percentage and date exactly as
        the input gives it); one sentence on what the customer offers in exchange; one sentence
        framing the timing; a courteous closing that invites a revised proposal, and a signature
        placeholder in square brackets with no digits. Firm, collaborative, concrete; about two
        hundred words, never more than three hundred. Plain text with line breaks; no markdown,
        no bullets other than a leading dash, no link, no citation key, no inline marker.

        usedCitationKeys lists the citation keys of every input item whose fact or wording the
        email uses, copied verbatim.

        """ + CommonLaws;

    /// <summary>The retry addendum when <see cref="DraftGuard"/> rejects a draft — names the
    /// violation exactly, the same "regenerate once with the violation named" policy the answer
    /// role uses (R-ASK-06).</summary>
    public static string BuildRetryInstruction(string violation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(violation);

        return
            "Your previous draft was rejected by an automated grounding check: " + violation +
            " Write it again using ONLY the numbers, dates and citation keys already present in " +
            "the input — leave out any figure the input does not carry, never write an inline " +
            "marker such as [1], never a link, never a key in the text.";
    }

    /// <summary>Strict schema (every property required, no additional properties) for the planner.</summary>
    public const string OfferPlanSchema =
        """
        {
          "type": "object",
          "properties": {
            "position": { "type": "string" },
            "asks": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "lever": { "type": "string" },
                  "sentence": { "type": "string" },
                  "citationKeys": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["lever", "sentence", "citationKeys"],
                "additionalProperties": false
              }
            },
            "trade": { "type": "string" },
            "deadlineAnchor": { "type": "string" },
            "closing": { "type": "string" }
          },
          "required": ["position", "asks", "trade", "deadlineAnchor", "closing"],
          "additionalProperties": false
        }
        """;

    /// <summary>Strict schema for the writer.</summary>
    public const string EmailDraftSchema =
        """
        {
          "type": "object",
          "properties": {
            "subject": { "type": "string" },
            "body": { "type": "string" },
            "usedCitationKeys": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["subject", "body", "usedCitationKeys"],
          "additionalProperties": false
        }
        """;
}
