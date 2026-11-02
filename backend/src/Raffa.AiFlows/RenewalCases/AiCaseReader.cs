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
    public const string ReplyVersion = "case-reply-v1";
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
              "required": ["key", "label", "state", "value"], "additionalProperties": false } }
          },
          "required": ["rows", "terms"],
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

    private sealed record ReplyAnswer(List<Row>? Rows, List<Term>? Terms);

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
        return new CaseReplyAnalysis(rows, terms);
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
