using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry.Wire;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// The <c>research</c> role (ADR-030): one Responses API call with exactly one hosted
/// <c>web_search</c> tool, on its own deployment (<see cref="AiGatewayModelOptions.Research"/>),
/// never on the answer deployment and never with a context pack. Sources are taken from the tool's
/// own <c>url_citation</c> annotations — a URL the model merely typed is not a source — and the
/// summary's <c>[n]</c> markers are resolved against the model's own <c>sources[]</c> list by
/// normalised URL (<see cref="WebSourceReconciler"/>, F3-T02). The call must end <c>completed</c>;
/// it has its own retry count and attempt timeout (F3-T03). Strict JSON
/// output (<c>summaryMarkdown</c>, <c>offTopic</c>, <c>sources</c>), the same structured-output
/// discipline every other role already uses. F3-T01: a <c>url_citation</c> annotation carries a URL
/// and a title and no page text, so each <c>sources[]</c> entry also carries <c>quote</c> — the
/// passage the model copied verbatim from that page — and the caller checks every figure of the
/// summary against it.
/// </summary>
public sealed class FoundryResearchClient(
    FoundryHttpJsonClient httpJsonClient,
    AiGatewayFoundryOptions foundryOptions,
    AiGatewayModelOptions modelOptions,
    IClock clock,
    AiGatewayResilienceOptions? resilienceOptions = null)
{
    private readonly AiGatewayResilienceOptions _resilience = resilienceOptions ?? new AiGatewayResilienceOptions();

    public const string WebSearchToolType = "web_search";
    public const string SchemaName = "raffa_web_research";
    public const string RelativeUrl = "openai/v1/responses";

    private static readonly string[] ForbiddenQueryFragments = ["citationKey", "packJson", "Context pack", "\"snippet\""];

    private static readonly JsonElement OutputSchema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summaryMarkdown", "offTopic", "sources"],
          "properties": {
            "summaryMarkdown": { "type": "string" },
            "offTopic": { "type": "boolean" },
            "sources": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["n", "url", "title", "quote"],
                "properties": {
                  "n": { "type": "integer" },
                  "url": { "type": "string" },
                  "title": { "type": "string" },
                  "quote": { "type": "string" }
                }
              }
            }
          }
        }
        """).RootElement.Clone();

    public async Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Result<AiResearchResult>.Failure("Research requires a non-empty query.");
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return Result<AiResearchResult>.Failure("Research requires a system prompt.");
        }

        if (request.MaxSources <= 0)
        {
            return Result<AiResearchResult>.Failure("Research requires MaxSources > 0.");
        }

        // Belt and braces on top of the request type's own shape: a query that looks like pack
        // JSON is never sent to the web.
        if (ForbiddenQueryFragments.Any(fragment => request.Query.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<AiResearchResult>.Failure("Research refuses a query that carries context-pack content (ADR-030).");
        }

        var model = modelOptions.Research;
        if (model is null)
        {
            return Result<AiResearchResult>.Failure(
                "AiGateway:Models:Research is not configured; the research role is unavailable (ADR-030 — it " +
                "never falls back to the answer deployment).");
        }

        if (!string.IsNullOrWhiteSpace(foundryOptions.OpenAiApiVersion))
        {
            return Result<AiResearchResult>.Failure(
                "The research role needs the openai/v1 Responses API; unset AiGateway:OpenAiApiVersion (the " +
                "date-based deployments route has no responses operation).");
        }

        var input =
            $"Language: {request.Language}\n" +
            $"Purpose: {request.Purpose}\n" +
            $"Max sources: {request.MaxSources}\n" +
            $"Query: {request.Query}";

        var toolType = string.IsNullOrWhiteSpace(foundryOptions.ResearchWebSearchToolType)
            ? WebSearchToolType
            : foundryOptions.ResearchWebSearchToolType.Trim();

        var wireRequest = new ResponsesRequest(
            model.ModelId,
            request.SystemPrompt,
            input,
            [new ResponsesTool(toolType)],
            new ResponsesText(new ResponsesTextFormat("json_schema", SchemaName, Strict: true, OutputSchema)),
            model.MaxCompletionTokens);

        // F3-T03: the research role has its own retry count and per-attempt timeout, apart from
        // extraction's (AiGateway:Resilience:MaxRetries / RequestTimeoutSeconds).
        var response = await httpJsonClient
            .PostAsync<ResponsesRequest, ResponsesResponse>(
                RelativeUrl,
                wireRequest,
                cancellationToken,
                maxRetriesOverride: _resilience.ResearchMaxRetries,
                attemptTimeout: TimeSpan.FromSeconds(Math.Max(1, _resilience.ResearchRequestTimeoutSeconds)))
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return Result<AiResearchResult>.Failure(response.Error);
        }

        // F3-T02: a Responses API call that stopped short (max_output_tokens, a content stop, a
        // provider failure) is a handled failure — never a truncated JSON that happens to parse.
        if (!string.Equals(response.Value.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var reason = response.Value.IncompleteDetails?.Reason;
            return Result<AiResearchResult>.Failure(
                $"{AiGatewayErrors.ResearchOutputPrefix} Foundry research on deployment '{model.ModelId}' did not complete " +
                $"(status {response.Value.Status ?? "unknown"}{(string.IsNullOrWhiteSpace(reason) ? string.Empty : ", " + reason)}).");
        }

        var message = response.Value.Output?
            .FirstOrDefault(item => string.Equals(item.Type, "message", StringComparison.OrdinalIgnoreCase));
        var content = message?.Content?
            .FirstOrDefault(c => string.Equals(c.Type, "output_text", StringComparison.OrdinalIgnoreCase));

        if (content?.Text is not { Length: > 0 } text)
        {
            return Result<AiResearchResult>.Failure(
                $"{AiGatewayErrors.ResearchOutputPrefix} Foundry research on deployment '{model.ModelId}' returned no output text (status {response.Value.Status ?? "unknown"}).");
        }

        ResearchPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ResearchPayload>(text, FoundryJsonOptions.Web);
        }
        catch (JsonException ex)
        {
            return Result<AiResearchResult>.Failure($"{AiGatewayErrors.ResearchOutputPrefix} Foundry research output was not the expected JSON: {ex.Message}");
        }

        if (payload is null)
        {
            return Result<AiResearchResult>.Failure($"{AiGatewayErrors.ResearchOutputPrefix} Foundry research output parsed to null.");
        }

        // F3-T02: the final list is the model's own sources[] (ordered by n) intersected, by
        // normalised URL, with the tool's url_citation annotations; markers are renumbered to it.
        var reconciled = payload.OffTopic
            ? new ReconciledResearch(string.Empty, [])
            : WebSourceReconciler.Reconcile(
                payload.SummaryMarkdown,
                (payload.Sources ?? []).Select(s => new ResearchModelSource(s.N, s.Url, s.Title, s.Quote)).ToList(),
                (content.Annotations ?? [])
                    .Where(a => string.Equals(a.Type, "url_citation", StringComparison.OrdinalIgnoreCase))
                    .Select(a => new ResearchToolCitation(a.Url, a.Title))
                    .ToList(),
                request.MaxSources);

        var usage = response.Value.Usage is { } u ? new AiTokenUsage(u.InputTokens, u.OutputTokens) : null;
        var metadata = FoundryCallMetadataFactory.Build(model, request.PromptVersion, clock, request.SystemPrompt + " " + input, usage);

        return Result<AiResearchResult>.Success(
            new AiResearchResult(reconciled.SummaryMarkdown, reconciled.Sources, payload.OffTopic, metadata));
    }

    private sealed record ResearchPayload(string? SummaryMarkdown, bool OffTopic, IReadOnlyList<ResearchPayloadSource>? Sources);

    private sealed record ResearchPayloadSource(int N, string? Url, string? Title, string? Quote);
}
