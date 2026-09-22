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
/// own <c>url_citation</c> annotations — a URL the model merely typed is not a source. Strict JSON
/// output (<c>summaryMarkdown</c>, <c>offTopic</c>, <c>sources</c>), the same structured-output
/// discipline every other role already uses.
/// </summary>
public sealed class FoundryResearchClient(
    FoundryHttpJsonClient httpJsonClient,
    AiGatewayFoundryOptions foundryOptions,
    AiGatewayModelOptions modelOptions,
    IClock clock)
{
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
                "required": ["n", "url", "title"],
                "properties": {
                  "n": { "type": "integer" },
                  "url": { "type": "string" },
                  "title": { "type": "string" }
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

        var response = await httpJsonClient
            .PostAsync<ResponsesRequest, ResponsesResponse>(RelativeUrl, wireRequest, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return Result<AiResearchResult>.Failure(response.Error);
        }

        var message = response.Value.Output?
            .FirstOrDefault(item => string.Equals(item.Type, "message", StringComparison.OrdinalIgnoreCase));
        var content = message?.Content?
            .FirstOrDefault(c => string.Equals(c.Type, "output_text", StringComparison.OrdinalIgnoreCase));

        if (content?.Text is not { Length: > 0 } text)
        {
            return Result<AiResearchResult>.Failure(
                $"Foundry research on deployment '{model.ModelId}' returned no output text (status {response.Value.Status ?? "unknown"}).");
        }

        ResearchPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ResearchPayload>(text, FoundryJsonOptions.Web);
        }
        catch (JsonException ex)
        {
            return Result<AiResearchResult>.Failure($"Foundry research output was not the expected JSON: {ex.Message}");
        }

        if (payload is null)
        {
            return Result<AiResearchResult>.Failure("Foundry research output parsed to null.");
        }

        // Only the tool's own citations count as sources; the payload's list may only re-title them.
        var titles = (payload.Sources ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .GroupBy(s => NormalizeUrl(s.Url!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Title ?? string.Empty, StringComparer.Ordinal);

        var sources = new List<AiWebSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var annotation in content.Annotations ?? [])
        {
            if (!string.Equals(annotation.Type, "url_citation", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(annotation.Url)
                || !seen.Add(NormalizeUrl(annotation.Url)))
            {
                continue;
            }

            var title = !string.IsNullOrWhiteSpace(annotation.Title)
                ? annotation.Title!
                : titles.TryGetValue(NormalizeUrl(annotation.Url), out var payloadTitle) && !string.IsNullOrWhiteSpace(payloadTitle)
                    ? payloadTitle
                    : annotation.Url;

            sources.Add(new AiWebSource(annotation.Url, title, Snippet: string.Empty));
            if (sources.Count == request.MaxSources)
            {
                break;
            }
        }

        var usage = response.Value.Usage is { } u ? new AiTokenUsage(u.InputTokens, u.OutputTokens) : null;
        var metadata = FoundryCallMetadataFactory.Build(model, request.PromptVersion, clock, request.SystemPrompt + " " + input, usage);

        return Result<AiResearchResult>.Success(
            new AiResearchResult(payload.OffTopic ? string.Empty : payload.SummaryMarkdown ?? string.Empty, sources, payload.OffTopic, metadata));
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');

    private sealed record ResearchPayload(string? SummaryMarkdown, bool OffTopic, IReadOnlyList<ResearchPayloadSource>? Sources);

    private sealed record ResearchPayloadSource(int N, string? Url, string? Title);
}
