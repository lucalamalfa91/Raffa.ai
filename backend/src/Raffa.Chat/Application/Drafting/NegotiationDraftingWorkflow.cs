using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;

namespace Raffa.Chat.Application.Drafting;

/// <summary>Where the draft came from — audited: a template fallback counts as a guard
/// intervention on the turn, so the golden set catches a fixture or prompt regression.</summary>
public enum DraftSource
{
    Model,
    Template,
}

/// <summary>What the composition root asks for: the question, its language, the supplier's
/// display name, the already-budgeted pack (the same Q3 pack a renewal-strategy turn gets, with
/// the council's plays inserted) and the parsed goal.</summary>
public sealed record DraftRequest(
    string Question,
    string Language,
    string SupplierName,
    IReadOnlyList<PackItem> Pack,
    SavingsGoal? Goal);

/// <summary>The drafted email plus everything the reply builder and the audit need.</summary>
/// <param name="UsedCitationKeys">Pack keys the email rests on — already filtered to keys that
/// exist in the pack.</param>
/// <param name="Attempts">Writer calls made (0 for a template-only draft).</param>
/// <param name="Metadata">The last writer call's reproducibility metadata (ADR-011), null for a
/// template draft.</param>
/// <param name="Failures">Agent failures along the way — logged, never shown to the user.</param>
public sealed record DraftOutcome(
    string Subject,
    string Body,
    IReadOnlyList<string> UsedCitationKeys,
    DraftSource Source,
    int Attempts,
    AiCallMetadata? Metadata,
    IReadOnlyList<string> Failures);

/// <summary>
/// The agentic workflow behind a drafted negotiation email (ADR-030 D3): round one, the offer
/// planner reads the pack (contract facts, lever calculations, council plays, market records,
/// playbook) and returns an offer plan; round two, the negotiation writer turns the plan into a
/// subject and a body; <see cref="DraftGuard"/> polices the result; one retry names the
/// violation; and a second failure — or a disabled workflow, a thin pack, a gateway outage —
/// falls back to <see cref="NegotiationEmailTemplate"/>. It never returns nothing: the user
/// always leaves with an email they can send.
/// </summary>
public sealed class NegotiationDraftingWorkflow(IAiGateway aiGateway, DraftingOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task<DraftOutcome> DraftAsync(DraftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SupplierName);
        ArgumentNullException.ThrowIfNull(request.Pack);

        var pack = request.Pack;
        var failures = new List<string>();

        if (!options.Enabled || pack.Count < options.MinPackItems)
        {
            return NegotiationEmailTemplate.Build(request.Language, request.SupplierName, pack, null);
        }

        var items = pack.Take(options.MaxItemsPerAgent).Select(ToItemDto).ToList();
        var goal = ToGoalDto(request.Goal);

        // ----- Round one: the offer planner -----
        var plan = await PlanAsync(request, items, goal, pack, failures, cancellationToken).ConfigureAwait(false)
            ?? DraftPlan.FromPack(pack);

        // ----- Round two: the writer, guarded, one retry -----
        var writerInput = JsonSerializer.Serialize(
            new WriterInput(request.Question, request.Language, request.SupplierName, goal, plan, items), JsonOptions);

        var first = await WriteAsync(DraftingAgents.NegotiationWriterPrompt, writerInput, cancellationToken).ConfigureAwait(false);
        if (first.Failure is not null)
        {
            failures.Add($"{DraftingAgents.NegotiationWriterName}: {first.Failure}");
            return NegotiationEmailTemplate.Build(request.Language, request.SupplierName, pack, plan) with { Failures = failures };
        }

        var firstVerdict = Validate(first.Payload!, pack);
        if (firstVerdict.Passed)
        {
            return Outcome(first.Payload!, first.Metadata!, pack, attempts: 1, failures);
        }

        failures.Add($"{DraftingAgents.NegotiationWriterName}: {firstVerdict.Violation}");

        var retryPrompt = DraftingAgents.NegotiationWriterPrompt + Environment.NewLine + Environment.NewLine +
                          DraftingAgents.BuildRetryInstruction(firstVerdict.Violation!);
        var retry = await WriteAsync(retryPrompt, writerInput, cancellationToken).ConfigureAwait(false);
        if (retry.Failure is null && Validate(retry.Payload!, pack).Passed)
        {
            return Outcome(retry.Payload!, retry.Metadata!, pack, attempts: 2, failures);
        }

        failures.Add(retry.Failure is not null
            ? $"{DraftingAgents.NegotiationWriterName} (retry): {retry.Failure}"
            : $"{DraftingAgents.NegotiationWriterName} (retry): {Validate(retry.Payload!, pack).Violation}");

        return NegotiationEmailTemplate.Build(request.Language, request.SupplierName, pack, plan) with
        {
            Attempts = 2,
            Failures = failures,
        };
    }

    private async Task<DraftPlan?> PlanAsync(
        DraftRequest request,
        IReadOnlyList<ItemDto> items,
        GoalDto? goal,
        IReadOnlyList<PackItem> pack,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Serialize(
            new PlannerInput(request.Question, request.Language, request.SupplierName, goal, items), JsonOptions);

        var result = await aiGateway.AnalyzeAsync(
                new AiAnalysisRequest(
                    DraftingAgents.OfferPlannerName,
                    DraftingAgents.OfferPlannerPrompt,
                    input,
                    DraftingAgents.OfferPlanSchema,
                    DraftingAgents.Version),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            failures.Add($"{DraftingAgents.OfferPlannerName}: {result.Error}");
            return null;
        }

        PlannerPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PlannerPayload>(result.Value.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            failures.Add($"{DraftingAgents.OfferPlannerName}: payload was not valid JSON — {ex.Message}");
            return null;
        }

        if (payload is null)
        {
            failures.Add($"{DraftingAgents.OfferPlannerName}: payload parsed to null.");
            return null;
        }

        // Local validation, no model call: an ask must cite the pack and carry only its numbers.
        var packKeys = pack.Select(i => i.CitationKey).ToHashSet(StringComparer.Ordinal);
        var asks = (payload.Asks ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Sentence))
            .Select(a => new DraftAsk(
                (a.Lever ?? string.Empty).Trim(),
                a.Sentence!.Trim(),
                (a.CitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList()))
            .Where(a => a.CitationKeys.Count > 0 && NumericGuard.Validate(a.Sentence, pack).Passed)
            .Take(4)
            .ToList();

        if (asks.Count == 0)
        {
            failures.Add($"{DraftingAgents.OfferPlannerName}: no grounded ask survived validation.");
            return null;
        }

        static string Clean(string? text, IReadOnlyList<PackItem> pack) =>
            !string.IsNullOrWhiteSpace(text) && NumericGuard.Validate(text, pack).Passed ? text.Trim() : string.Empty;

        return new DraftPlan(
            Clean(payload.Position, pack),
            asks,
            Clean(payload.Trade, pack),
            Clean(payload.DeadlineAnchor, pack),
            Clean(payload.Closing, pack));
    }

    private async Task<(WriterPayload? Payload, AiCallMetadata? Metadata, string? Failure)> WriteAsync(
        string systemPrompt, string inputJson, CancellationToken cancellationToken)
    {
        var result = await aiGateway.AnalyzeAsync(
                new AiAnalysisRequest(
                    DraftingAgents.NegotiationWriterName,
                    systemPrompt,
                    inputJson,
                    DraftingAgents.EmailDraftSchema,
                    DraftingAgents.Version),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return (null, null, result.Error);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<WriterPayload>(result.Value.PayloadJson, JsonOptions);
            return payload is null
                ? (null, null, "payload parsed to null.")
                : (payload, result.Value.Metadata, null);
        }
        catch (JsonException ex)
        {
            return (null, null, $"payload was not valid JSON — {ex.Message}");
        }
    }

    private GuardVerdict Validate(WriterPayload payload, IReadOnlyList<PackItem> pack) =>
        DraftGuard.Validate(payload.Subject, payload.Body, payload.UsedCitationKeys, pack, options.MaxBodyChars);

    private static DraftOutcome Outcome(
        WriterPayload payload, AiCallMetadata metadata, IReadOnlyList<PackItem> pack, int attempts, List<string> failures) =>
        new(
            payload.Subject!.Trim(),
            payload.Body!.Trim(),
            DraftGuard.GroundedKeys(payload.UsedCitationKeys, pack),
            DraftSource.Model,
            attempts,
            metadata,
            failures);

    private static GoalDto? ToGoalDto(SavingsGoal? goal) =>
        goal is null || !goal.HasTarget
            ? null
            : new GoalDto(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel);

    private static ItemDto ToItemDto(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new ValueDto(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());

    // ----- wire shapes (camelCase JSON; the fixture gateway mirrors "items"/"plan" structurally) -----

    private sealed record GoalDto(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

    private sealed record ValueDto(string Key, string Value, string Kind, string? Currency);

    private sealed record ItemDto(string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<ValueDto> Values);

    private sealed record PlannerInput(string Question, string Language, string Supplier, GoalDto? Goal, IReadOnlyList<ItemDto> Items);

    private sealed record WriterInput(string Question, string Language, string Supplier, GoalDto? Goal, DraftPlan Plan, IReadOnlyList<ItemDto> Items);

    private sealed record PlannerPayload(string? Position, IReadOnlyList<PlannerAsk>? Asks, string? Trade, string? DeadlineAnchor, string? Closing);

    private sealed record PlannerAsk(string? Lever, string? Sentence, IReadOnlyList<string>? CitationKeys);

    private sealed record WriterPayload(string? Subject, string? Body, IReadOnlyList<string>? UsedCitationKeys);
}
