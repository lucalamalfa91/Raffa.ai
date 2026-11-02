using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Raffa.SharedKernel.Text;

namespace Raffa.AiFlows.RenewalCases;

/// <summary>The two prompts of the renewal case readings: the supplier reply read against the plan and the
/// case terms, and the functions an alternative supplier must cover. One <c>analyst</c>-role call each,
/// strict JSON; the caller validates and keeps the deterministic reading on any doubt.</summary>
public static class CaseReaderAgents
{
    public const string ReplyName = "case-reply-reader";
    public const string ReplyVersion = "case-reply-v2";
    public const string FeaturesName = "case-features-reader";
    public const string FeaturesVersion = "case-features-v1";

    public const string ReplyPrompt =
        """
        You are Raffa, the contract assistant of a procurement team running a contract renewal. The
        supplier answered; you read the reply against what the team asked (plan, goals) and against the
        terms tracked in the case (terms: key, label, state, value). contract holds the current
        contract facts (supplier, products with unit prices, term dates, uplift, auto-renewal).

        For every point the reply touches (price, term, uplift, payment terms, liability, any clause in
        the plan or in terms), write one row:
        - label: the point, short, in the user's language;
        - said: what the supplier said, with their exact numbers;
        - plan: what the team asked for that point ("not in the plan" when nothing);
        - state: proposed (a concrete offer), unclear (vague or conditional without numbers),
          rejected (they refuse the ask), conflict (they ask something against the plan or policy);
        - warn: a short policy or risk warning when the offer breaks the plan or a usual safeguard, else "".
        Also benchmark: the commercial terms the supplier states in this reply, as plain numbers, from the
        supplier's words only (0 or "" when the reply does not say): discountPct (the percentage off the current
        price it offers), upliftCapPct (the cap on yearly price increases it accepts), noticeDays (the notice
        period it asks), termMonths (the term its offer is for), paymentTerms (a short phrase such as
        "quarterly in advance"; never a name or an email).
        Also prices: the unit price the supplier now offers for a 12-month term (price12) and for a
        24-month term (price24), as plain numbers in the contract currency; 0 when the reply gives none.
        Then the term updates the reply implies: key (an existing term key when the point is tracked,
        else a short lowercase key such as price, term, uplift, payment, liab), label, state
        (proposed | unclear | rejected), value (what the supplier now offers, or "" ).

        Laws (they override everything else):
        1. Use only the reply, the plan, the terms and the contract. Never invent a number or a promise.
        2. Never mark anything agreed: only the people in the team agree.
        3. Points the reply does not mention get no row.
        4. Write in the user's language (language).
        5. Respond with strict JSON matching the schema only — no prose outside the JSON.
        """;

    public const string ReplySchema =
        """
        {
          "type": "object",
          "properties": {
            "rows": { "type": "array", "items": { "type": "object", "properties": {
              "label": { "type": "string" }, "said": { "type": "string" }, "plan": { "type": "string" },
              "state": { "type": "string", "enum": ["proposed", "unclear", "rejected", "conflict"] },
              "warn": { "type": "string" } },
              "required": ["label", "said", "plan", "state", "warn"], "additionalProperties": false } },
            "terms": { "type": "array", "items": { "type": "object", "properties": {
              "key": { "type": "string" }, "label": { "type": "string" },
              "state": { "type": "string", "enum": ["proposed", "unclear", "rejected"] },
              "value": { "type": "string" } },
              "required": ["key", "label", "state", "value"], "additionalProperties": false } },
            "price12": { "type": "number" },
            "price24": { "type": "number" },
            "benchmark": { "type": "object", "properties": {
              "discountPct": { "type": "number" }, "upliftCapPct": { "type": "number" },
              "noticeDays": { "type": "integer" }, "termMonths": { "type": "integer" },
              "paymentTerms": { "type": "string" } },
              "required": ["discountPct", "upliftCapPct", "noticeDays", "termMonths", "paymentTerms"], "additionalProperties": false }
          },
          "required": ["rows", "terms", "price12", "price24", "benchmark"],
          "additionalProperties": false
        }
        """;

    public const string DraftName = "case-draft-checker";
    public const string DraftVersion = "case-draft-v1";
    public const string SignedName = "case-signed-checker";
    public const string SignedVersion = "case-signed-v1";
    public const string OfferName = "case-offer-reader";
    public const string OfferVersion = "case-offer-v1";

    public const string DraftPrompt =
        """
        You are Raffa, the contract assistant of a procurement team. The supplier sent a contract draft
        (document) after the renewal terms were agreed (agreed: unit price, term in months, uplift cap in %,
        liability cap in months of fees, payment terms when known; terms: what the negotiation settled).
        Compare the draft with every agreed point and write one row per point:
        - label: the point, in the user's language;
        - agreed: the agreed value;
        - draft: what the draft says, quoted with its numbers ("" when you cannot find it);
        - result: ok (the draft matches), diff (it differs), unread (the point is not in the readable text);
        - source: where you read it (page or section heading), "" when unknown.
        Laws: use only the document, agreed and terms; never guess a value you did not read (use unread);
        write in the user's language (language); respond with strict JSON matching the schema only.
        """;

    public const string DraftSchema =
        """
        {
          "type": "object",
          "properties": {
            "rows": { "type": "array", "items": { "type": "object", "properties": {
              "label": { "type": "string" }, "agreed": { "type": "string" }, "draft": { "type": "string" },
              "result": { "type": "string", "enum": ["ok", "diff", "unread"] }, "source": { "type": "string" } },
              "required": ["label", "agreed", "draft", "result", "source"], "additionalProperties": false } }
          },
          "required": ["rows"],
          "additionalProperties": false
        }
        """;

    public const string SignedPrompt =
        """
        You are Raffa, the contract assistant of a procurement team. This is the signed contract (document).
        Check it against the agreed position (agreed) and the rows of the last verified draft (lastDraft):
        count the points you could check (points) and how many differ from what was agreed (differences).
        Laws: use only the document, agreed and lastDraft; a point you cannot read is not counted as checked;
        respond with strict JSON matching the schema only.
        """;

    public const string SignedSchema =
        """
        {
          "type": "object",
          "properties": { "points": { "type": "integer" }, "differences": { "type": "integer" } },
          "required": ["points", "differences"],
          "additionalProperties": false
        }
        """;

    public const string OfferPrompt =
        """
        You are Raffa, the contract assistant of a procurement team comparing an alternative supplier (vendor)
        with the current contract. Read the offer (document) against the functions the team needs
        (requirements) and the current sizing (contract.products). Give:
        - price: the unit price per unit and month comparable with the current contract, 0 if not stated;
        - total: the yearly total of the offer, 0 if not stated;
        - term: the term in months, 0 if not stated;
        - cover: how the offer covers the requirements, one short sentence ("not stated" when silent);
        - migration: the migration plan and its cost, one short sentence ("not stated" when silent);
        - gap: the most important requirement not covered or unclear, one short sentence ("" when none).
        Laws: use only the offer, requirements and contract; never invent a price or a promise; write in the
        user's language (language); respond with strict JSON matching the schema only.
        """;

    public const string OfferSchema =
        """
        {
          "type": "object",
          "properties": {
            "price": { "type": "number" }, "total": { "type": "number" }, "term": { "type": "integer" },
            "cover": { "type": "string" }, "migration": { "type": "string" }, "gap": { "type": "string" }
          },
          "required": ["price", "total", "term", "cover", "migration", "gap"],
          "additionalProperties": false
        }
        """;

    public const string FeaturesPrompt =
        """
        You are Raffa, the contract assistant of a procurement team looking for an alternative supplier.
        From the current contract (its products, key terms, clauses and obligations) list the functions
        a replacement must cover, phrased as functions, never as product or brand names (for example
        "Electronic signature with audit trail", not "DocuSign eSignature"). Also give the category in
        English, lowercase, two or three words (for example "electronic signature"), or "" if the contract
        does not say.

        Laws (they override everything else):
        1. Use only what the contract says; never add a function the contract does not support.
        2. Between 3 and 8 functions, each under 70 characters, in the user's language (language).
        3. Respond with strict JSON matching the schema only — no prose outside the JSON.
        """;

    public const string FeaturesSchema =
        """
        {
          "type": "object",
          "properties": {
            "categoryEn": { "type": "string" },
            "functions": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["categoryEn", "functions"],
          "additionalProperties": false
        }
        """;
}

/// <summary>The model-backed <see cref="ICaseAiReader"/>: reads the supplier reply and the functions an
/// alternative must cover. Any failure, timeout or off-schema answer returns null (the deterministic
/// reading stands).</summary>
public sealed class AiCaseReader(IAiGateway aiGateway, ITenantContext tenantContext, ILogger<AiCaseReader>? logger = null) : ICaseAiReader
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> RowStates = ["proposed", "unclear", "rejected", "conflict"];

    private static readonly HashSet<string> TermStates = ["proposed", "unclear", "rejected"];

    private sealed record Row(string? Label, string? Said, string? Plan, string? State, string? Warn);

    private sealed record Term(string? Key, string? Label, string? State, string? Value);

    private sealed record BenchmarkAnswer(double? DiscountPct, double? UpliftCapPct, int? NoticeDays, int? TermMonths, string? PaymentTerms);

    private sealed record ReplyAnswer(List<Row>? Rows, List<Term>? Terms, double? Price12, double? Price24, BenchmarkAnswer? Benchmark);

    private sealed record DraftRowAnswer(string? Label, string? Agreed, string? Draft, string? Result, string? Source);

    private sealed record DraftAnswer(List<DraftRowAnswer>? Rows);

    private sealed record SignedAnswer(int? Points, int? Differences);

    private sealed record OfferAnswer(double? Price, double? Total, int? Term, string? Cover, string? Migration, string? Gap);

    private const int MaxDocumentChars = 60_000;

    private static string Clip(string text) => text.Length > MaxDocumentChars ? text[..MaxDocumentChars] : text;

    private static object Agreed(RenewalCase c) => new
    {
        unitPrice = c.Agreed?.Price,
        termMonths = c.Agreed?.Term,
        upliftCapPercent = c.Agreed?.Cap,
        liabilityCapMonths = c.Agreed?.Liab,
    };

    private static object Terms(RenewalCase c) =>
        c.Terms.Select(kv => new { key = kv.Key, label = kv.Value.Label.Render(), state = kv.Value.State, value = kv.Value.Val });

    public async Task<IReadOnlyList<CaseDraftRow>?> ReadDraftAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        var input = JsonSerializer.Serialize(new { language = lang, fileName, agreed = Agreed(c), terms = Terms(c), document = Clip(documentText) }, Json);
        var answer = Parse<DraftAnswer>(await CallAsync(tenantId, CaseReaderAgents.DraftName, CaseReaderAgents.DraftPrompt, input,
            CaseReaderAgents.DraftSchema, CaseReaderAgents.DraftVersion, cancellationToken).ConfigureAwait(false));
        var rows = (answer?.Rows ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Label) && r.Result is "ok" or "diff" or "unread")
            .Take(12)
            .Select(r => new CaseDraftRow
            {
                Label = CaseText.Of(r.Label!.Trim()),
                Agreed = CaseText.Of((r.Agreed ?? string.Empty).Trim()),
                // A point not found is never shown as a value: the engine's "unread" path handles it.
                Draft = CaseText.Of(r.Result == "unread" || string.IsNullOrWhiteSpace(r.Draft) ? "—" : r.Draft.Trim()),
                Res = r.Result!,
                Src = (r.Source ?? string.Empty).Trim(),
            })
            .ToList();
        return rows.Count == 0 ? null : rows;
    }

    public async Task<CaseSignedCheck?> ReadSignedAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        var lastDraft = c.Docs.LastOrDefault()?.Rows.Select(r => new { label = r.Label.Render(), agreed = r.Agreed.Render(), draft = r.Draft.Render(), result = r.Res });
        var input = JsonSerializer.Serialize(new { language = lang, fileName, agreed = Agreed(c), lastDraft, document = Clip(documentText) }, Json);
        var answer = Parse<SignedAnswer>(await CallAsync(tenantId, CaseReaderAgents.SignedName, CaseReaderAgents.SignedPrompt, input,
            CaseReaderAgents.SignedSchema, CaseReaderAgents.SignedVersion, cancellationToken).ConfigureAwait(false));
        return answer is { Points: > 0 and var points } && answer.Differences is >= 0 and var diff && diff <= points
            ? new CaseSignedCheck(points, diff)
            : null;
    }

    public async Task<CaseAltOffer?> ReadOfferAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string vendor, string documentText, string by, string lang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(contract);
        var requirements = (c.Alt?.Reqs ?? []).Where(r => r.On).Select(r => r.Text.Render());
        var input = JsonSerializer.Serialize(new { language = lang, vendor, requirements, contract = Describe(contract), document = Clip(documentText) }, Json);
        var answer = Parse<OfferAnswer>(await CallAsync(tenantId, CaseReaderAgents.OfferName, CaseReaderAgents.OfferPrompt, input,
            CaseReaderAgents.OfferSchema, CaseReaderAgents.OfferVersion, cancellationToken).ConfigureAwait(false));
        if (answer is null || (answer.Price is not > 0 && answer.Total is not > 0 && string.IsNullOrWhiteSpace(answer.Cover)))
        {
            return null;
        }

        return new CaseAltOffer
        {
            Price = answer.Price is > 0 ? answer.Price.Value : 0,
            Total = answer.Total is > 0 ? answer.Total.Value : 0,
            Term = answer.Term is > 0 ? answer.Term.Value : 0,
            Cover = string.IsNullOrWhiteSpace(answer.Cover) ? "not stated" : answer.Cover.Trim(),
            Mig = string.IsNullOrWhiteSpace(answer.Migration) ? "not stated" : answer.Migration.Trim(),
            Gap = (answer.Gap ?? string.Empty).Trim(),
            By = by,
        };
    }

    private sealed record FeaturesAnswer(string? CategoryEn, List<string>? Functions);

    public async Task<CaseReplyAnalysis?> ReadReplyAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string replyText, int round, string lang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(contract);
        var input = JsonSerializer.Serialize(new
        {
            language = lang,
            round,
            reply = replyText.Length > 20_000 ? replyText[..20_000] : replyText,
            plan = new
            {
                products = c.Goals.Prod ?? c.Goals.ProdText,
                term = c.Goals.Term == "custom" ? c.Goals.TermText : c.Goals.Term,
                target = c.Goals.Target == "custom" ? c.Goals.TargetText : c.Goals.Target,
                clauses = c.Goals.Clauses,
                followCompanyPolicy = c.Goals.Policy,
            },
            terms = c.Terms.Select(kv => new { key = kv.Key, label = kv.Value.Label.Render(), state = kv.Value.State, value = kv.Value.Val }),
            contract = Describe(contract),
        }, Json);
        var answer = Parse<ReplyAnswer>(await CallAsync(tenantId, CaseReaderAgents.ReplyName, CaseReaderAgents.ReplyPrompt, input,
            CaseReaderAgents.ReplySchema, CaseReaderAgents.ReplyVersion, cancellationToken).ConfigureAwait(false));
        var rows = (answer?.Rows ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Label) && !string.IsNullOrWhiteSpace(r.Said) && r.State is not null && RowStates.Contains(r.State))
            .Take(12)
            .Select(r => new CaseReplyRow
            {
                Label = CaseText.Of(r.Label!.Trim()),
                Said = CaseText.Of(r.Said!.Trim()),
                Plan = CaseText.Of((r.Plan ?? string.Empty).Trim()),
                State = r.State!,
                Warn = string.IsNullOrWhiteSpace(r.Warn) ? CaseText.Empty : CaseText.Of(r.Warn.Trim()),
            })
            .ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var ev = "reply " + round.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var terms = (answer!.Terms ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Key) && !string.IsNullOrWhiteSpace(t.Label) && t.State is not null && TermStates.Contains(t.State))
            .Take(12)
            .Select(t => new CaseTermUpdate(
                t.Key!.Trim().ToLowerInvariant(),
                CaseText.Of(t.Label!.Trim()),
                t.State!,
                string.IsNullOrWhiteSpace(t.Value) ? null : t.Value.Trim(),
                ev))
            .ToList();
        var prices = new Dictionary<int, double>();
        if (answer.Price12 is > 0 and var p12)
        {
            prices[12] = p12;
        }

        if (answer.Price24 is > 0 and var p24)
        {
            prices[24] = p24;
        }

        // Zero means "not stated" in the schema: only real values go on to the benchmark.
        var b = answer.Benchmark;
        var benchmark = b is null
            ? null
            : new CaseReplyBenchmark(
                b.DiscountPct is > 0 ? b.DiscountPct : null,
                b.UpliftCapPct is > 0 ? b.UpliftCapPct : null,
                b.NoticeDays is > 0 ? b.NoticeDays : null,
                string.IsNullOrWhiteSpace(b.PaymentTerms) ? null : b.PaymentTerms.Trim(),
                b.TermMonths is > 0 ? b.TermMonths : null);
        return new CaseReplyAnalysis(rows, terms, prices.Count == 0 ? null : prices, benchmark);
    }

    public async Task<CaseFeatureSet?> ReadFeaturesAsync(TenantId tenantId, CaseContractFacts contract, string lang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var input = JsonSerializer.Serialize(new { language = lang, contract = Describe(contract) }, Json);
        var answer = Parse<FeaturesAnswer>(await CallAsync(tenantId, CaseReaderAgents.FeaturesName, CaseReaderAgents.FeaturesPrompt, input,
            CaseReaderAgents.FeaturesSchema, CaseReaderAgents.FeaturesVersion, cancellationToken).ConfigureAwait(false));
        var items = (answer?.Functions ?? [])
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => x.Length is > 0 and <= 120)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(CaseText.Of)
            .ToList();
        if (items.Count < 2)
        {
            return null;
        }

        var category = answer!.CategoryEn?.Trim();
        return new CaseFeatureSet(string.IsNullOrWhiteSpace(category) ? null : category.ToLowerInvariant(), items);
    }

    private static object Describe(CaseContractFacts f) => new
    {
        supplier = f.Supplier,
        name = f.Name,
        type = f.Type,
        start = f.Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        end = f.End.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        noticeDays = f.Notice,
        autoRenewal = f.Auto,
        uplift = f.Uplift,
        annualSpend = f.Spend,
        products = f.Products.Select(p => new { name = p.Name, unit = p.Unit, quantity = p.Qty, unitPrice = p.Price }),
        keyTerms = f.Terms.Select(t => new { label = t.Label, value = t.Value }),
        clausesToPush = f.Push.Select(x => new { topic = x.Type, says = x.Norm, ask = x.Ask }),
        clausesToRaise = f.Raise.Select(x => new { topic = x.Type, says = x.Norm, ask = x.Ask }),
        ourObligations = f.OblYou.Select(o => o.Text),
        supplierObligations = f.OblSup.Select(o => o.Text),
    };

    private async Task<string?> CallAsync(TenantId tenantId, string name, string prompt, string input, string schema, string version, CancellationToken cancellationToken)
    {
        try
        {
            using var tenantScope = tenantContext.BeginScope(tenantId);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            var result = await aiGateway.AnalyzeAsync(new AiAnalysisRequest(name, prompt, input, schema, version), cts.Token).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger?.LogInformation("Case reading {Name}: model unavailable ({Error}).", name, result.Error);
                return null;
            }

            return result.Value.PayloadJson;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Case reading {Name}: model timed out.", name);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Case reading {Name}: model call failed.", name);
            return null;
        }
    }

    private static T? Parse<T>(string? payload)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class CaseReaderFlowServiceCollectionExtensions
{
    public static IServiceCollection AddCaseReaderFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Scoped<ICaseAiReader, AiCaseReader>());
        return services;
    }
}
