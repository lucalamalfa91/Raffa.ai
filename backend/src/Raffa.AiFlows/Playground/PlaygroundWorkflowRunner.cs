using System.Diagnostics;
using System.Text.Json;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Parsing;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.AiFlows.WebResearch.Query;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;

namespace Raffa.AiFlows.Playground;

/// <summary>The outcome of one playground workflow: a JSON-ready <see cref="Output"/>, or why it could not run.</summary>
public sealed record PlaygroundWorkflowResult(object? Output, string? Error)
{
    public static PlaygroundWorkflowResult Ok(object output) => new(output, null);

    public static PlaygroundWorkflowResult Fail(string error) => new(null, error);
}

/// <summary>
/// The Super Admin playground's entry points into the agentic workflows other than the Ask turn
/// (docs/architecture/admin-agent-playground-plan.md): the negotiation council, the drafting writer, web
/// research and document extraction. Each runs the production flow object with its production collaborators
/// over a sandbox workspace's data or text the admin supplies, and returns what the flow produced so the
/// playground can show it next to the trace. Nothing here writes a contract, a document or a fact: the
/// extraction rerun reads text and returns the model's payload per stage, it never goes through the run store.
/// Reachable only from the platform-admin playground endpoints.
/// </summary>
public sealed class PlaygroundWorkflowRunner(
    PortfolioQueryService portfolio,
    ISupplierNameLookup supplierNames,
    SavingsPackBuilder savings,
    MarketDataCheck dataCheck,
    NegotiationCouncil council,
    NegotiationDraftingWorkflow drafting,
    WebResearchComposer webResearch,
    IOptionsMonitor<WebResearchOptions> webOptions,
    IEnumerable<IExtractionStage> extractionStages,
    IAiGateway aiGateway)
{
    public const int MaxDocumentChars = 400_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> WebPurposes { get; } = ["MarketPractice", "SupplierNews", "BenchmarkRange", "NegotiationTactics", "Open"];

    // ----- negotiation council -----

    public async Task<PlaygroundWorkflowResult> RunCouncilAsync(
        TenantId tenantId, string supplier, string? goalText, string question, string actor, CancellationToken ct)
    {
        var (pack, goal, name, error) = await BuildPackAsync(tenantId, supplier, goalText, actor, ct).ConfigureAwait(false);
        if (pack is null)
        {
            return PlaygroundWorkflowResult.Fail(error!);
        }

        var outcome = await council.RunAsync(question, pack, goal, ct).ConfigureAwait(false);
        return PlaygroundWorkflowResult.Ok(new
        {
            supplier = name,
            goal = goalText,
            agentsRun = outcome.AgentsRun,
            failures = outcome.Failures,
            playsDropped = outcome.PlaysDropped,
            plays = outcome.Items.Select(Item),
            evidence = pack.Select(Item),
        });
    }

    // ----- negotiation email drafting -----

    public async Task<PlaygroundWorkflowResult> RunDraftingAsync(
        TenantId tenantId, string supplier, string? goalText, string question, string language, string actor, CancellationToken ct)
    {
        var (pack, goal, name, error) = await BuildPackAsync(tenantId, supplier, goalText, actor, ct).ConfigureAwait(false);
        if (pack is null)
        {
            return PlaygroundWorkflowResult.Fail(error!);
        }

        var outcome = await drafting.DraftAsync(new DraftRequest(question, language, name!, pack, goal), ct).ConfigureAwait(false);
        return PlaygroundWorkflowResult.Ok(new
        {
            supplier = name,
            subject = outcome.Subject,
            body = outcome.Body,
            source = outcome.Source.ToString(),
            type = outcome.Type.ToString(),
            attempts = outcome.Attempts,
            usedCitationKeys = outcome.UsedCitationKeys,
            toneFindings = outcome.ToneFindings ?? [],
            failures = outcome.Failures,
            evidence = pack.Select(Item),
        });
    }

    // ----- web research -----

    /// <summary>The research core (query sanitising, the research role, the figure and sentence guards), without the
    /// workspace opt-in, the daily budget and the consent card: the admin is the consent here.</summary>
    public async Task<PlaygroundWorkflowResult> RunWebResearchAsync(string query, string purpose, string language, CancellationToken ct)
    {
        if (!WebPurposes.Contains(purpose, StringComparer.Ordinal))
        {
            return PlaygroundWorkflowResult.Fail($"purpose must be one of: {string.Join(", ", WebPurposes)}.");
        }

        // The same sanitiser the consent gate applies in production (stopwords, punctuation, sensitive patterns, length):
        // what leaves Raffa is the cleaned query, never the raw text the admin typed.
        var options = webOptions.CurrentValue;
        var cleaned = WebQuerySanitizer.Sanitize(query, options.MaxQueryChars, options.RedactSensitiveQueryPatterns);
        if (cleaned is null)
        {
            return PlaygroundWorkflowResult.Fail($"The query is too short once cleaned (at least {WebQuerySanitizer.MinWords} words of substance).");
        }

        var outcome = await webResearch.ComposeAsync(cleaned, purpose, language, ct).ConfigureAwait(false);
        return PlaygroundWorkflowResult.Ok(new
        {
            sentQuery = cleaned,
            kind = outcome.Kind.ToString(),
            markdown = outcome.Markdown,
            sourceCount = outcome.SourceCount,
            citations = outcome.Citations.Select(c => new { c.N, c.Title, c.Href, c.Snippet }),
            guardIntervened = outcome.GuardIntervened,
            guardViolation = outcome.GuardViolation,
            figuresVerified = outcome.FiguresVerified,
            figuresReported = outcome.FiguresReported,
            sentencesRemoved = outcome.SentencesRemoved,
            error = outcome.Error,
        });
    }

    // ----- document extraction -----

    /// <summary>OCR of an uploaded file (in memory; nothing is stored), as the pages the extraction stages read.</summary>
    public async Task<(IReadOnlyList<DocumentPageText>? Pages, string? Error)> ReadFileAsync(
        string fileName, string mimeType, byte[] content, CancellationToken ct)
    {
        var ocr = await aiGateway.OcrAsync(new AiOcrRequest(fileName, mimeType, content), ct).ConfigureAwait(false);
        return ocr.IsFailure
            ? (null, ocr.Error)
            : ([.. ocr.Value.Pages.Select(p => new DocumentPageText(p.PageNumber, p.Text))], null);
    }

    /// <summary>
    /// Runs the staged extraction over <paramref name="pages"/> and returns each stage's raw payload as the model wrote
    /// it, plus the facts counted per stage. Scalar stages first (in parallel), then the stages that read their facts, as
    /// the pipeline schedules them. No span verification, no cross-stage validation and nothing is persisted: it shows
    /// what a prompt or a model change does to the extraction itself.
    /// </summary>
    public async Task<PlaygroundWorkflowResult> RunExtractionAsync(
        IReadOnlyList<DocumentPageText> pages, bool documentFirst, CancellationToken ct)
    {
        if (pages.Count == 0 || pages.All(p => string.IsNullOrWhiteSpace(p.Text)))
        {
            return PlaygroundWorkflowResult.Fail("The document has no readable text.");
        }

        var text = PageMarkedText.Build(pages);
        if (text.Length > MaxDocumentChars)
        {
            return PlaygroundWorkflowResult.Fail($"The document text is longer than {MaxDocumentChars} characters.");
        }

        var byStage = extractionStages.ToDictionary(s => s.Stage);
        var order = ExtractionPipeline.Stages.Where(byStage.ContainsKey).ToList();
        var done = new Dictionary<ExtractionStage, StageFacts>();
        var results = new Dictionary<ExtractionStage, object>();
        var clock = Stopwatch.StartNew();

        while (done.Count + results.Count(r => !done.ContainsKey(r.Key)) < order.Count)
        {
            var ready = order
                .Where(stage => !results.ContainsKey(stage) && byStage[stage].DependsOn.All(dep => done.ContainsKey(dep) || !byStage.ContainsKey(dep) || results.ContainsKey(dep)))
                .ToList();
            if (ready.Count == 0)
            {
                break;
            }

            var context = new Dictionary<ExtractionStage, StageFacts>(done);
            var wave = await Task.WhenAll(ready.Select(stage => RunStageAsync(byStage[stage], text, pages.Count, context, documentFirst, ct))).ConfigureAwait(false);
            foreach (var (stage, result, facts) in wave)
            {
                results[stage] = result;
                if (facts is not null)
                {
                    done[stage] = facts;
                }
            }
        }

        return PlaygroundWorkflowResult.Ok(new
        {
            pages = pages.Count,
            characters = text.Length,
            documentFirst,
            // The text the stages read (cut at 30,000 characters): the evidence a judge needs to check the fields.
            sourceText = text.Length > 30_000 ? text[..30_000] : text,
            durationMs = clock.ElapsedMilliseconds,
            stages = order.Where(results.ContainsKey).Select(s => results[s]),
        });
    }

    private async Task<(ExtractionStage Stage, object Result, StageFacts? Facts)> RunStageAsync(
        IExtractionStage stage, string text, int pageCount, IReadOnlyDictionary<ExtractionStage, StageFacts> context, bool documentFirst, CancellationToken ct)
    {
        var request = stage.BuildRequest(new ExtractionStageInput(text, pageCount, context, documentFirst));
        var response = await aiGateway.ExtractAsync(request, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return (stage.Stage, new { stage = stage.StageName, ok = false, error = response.Error, payload = (JsonElement?)null }, null);
        }

        StageFacts? facts = null;
        string? parseError = null;
        try
        {
            facts = stage.Parse(response.Value.PayloadJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            parseError = ex.GetType().Name + ": " + ex.Message;
        }

        return (stage.Stage, new
        {
            stage = stage.StageName,
            ok = parseError is null,
            error = parseError,
            model = response.Value.Metadata.ModelId,
            payload = ParseJson(response.Value.PayloadJson),
        }, facts);
    }

    // ----- shared -----

    private async Task<(List<PackItem>? Pack, SavingsGoal? Goal, string? Supplier, string? Error)> BuildPackAsync(
        TenantId tenantId, string supplier, string? goalText, string actor, CancellationToken ct)
    {
        var page = await portfolio.GetPortfolioAsync(tenantId, new PortfolioFilter(), new PortfolioPageRequest(1, 100), ct).ConfigureAwait(false);
        var ids = page.Items.Where(i => i.SupplierId is not null).Select(i => new EntityId(i.SupplierId!.Value)).Distinct().ToList();
        var names = await supplierNames.GetNamesAsync(tenantId, ids, ct).ConfigureAwait(false);
        var match = page.Items
            .Where(i => i.SupplierId is not null && names.ContainsKey(new EntityId(i.SupplierId.Value)))
            .Select(i => (Item: i, Name: names[new EntityId(i.SupplierId!.Value)]))
            .FirstOrDefault(x => string.Equals(x.Name, supplier.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match.Item is null)
        {
            var known = names.Values.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return (null, null, null, known.Count == 0
                ? "This sandbox has no contracts. Pick the 'seeded' or 'needs-review' sandbox."
                : $"No contract for '{supplier}' in this sandbox. Suppliers: {string.Join(", ", known)}.");
        }

        var goal = string.IsNullOrWhiteSpace(goalText) ? null : SavingsGoalParser.Parse(goalText);
        var pack = (await savings.BuildSavingsLeverPackAsync(tenantId, match.Item, goal, actor, ct).ConfigureAwait(false)).ToList();
        var check = await dataCheck.RunAsync([match.Item], [], ct).ConfigureAwait(false);
        pack.AddRange(check.Items.Where(i => pack.All(x => x.CitationKey != i.CitationKey)));
        return (pack, goal, match.Name, null);
    }

    private static object Item(PackItem item) => new
    {
        citationKey = item.CitationKey,
        corpus = item.Corpus,
        title = item.Title,
        snippet = item.Snippet,
        provenance = item.Provenance,
        values = item.Values.Select(v => new { v.Key, v.Value }),
    };

    private static JsonElement? ParseJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
