using System.Text.Json;
using System.Text.Json.Serialization;
using Contigo.AiGateway;
using Contigo.AiGateway.Contracts;
using Contigo.Chat.Application.Guards;
using Contigo.Chat.Application.Pack;
using Contigo.SharedKernel;

namespace Contigo.Chat.Application.Answering;

/// <summary>
/// Calls the `answer` role with the assembled context pack, the versioned V2 persona prompt and
/// the conversation's recent turns (task E13/F06/US01/T01, ask-engine coding objective point 4:
/// "AnswerComposer calling IAiGateway.AnswerAsync with the pack + last N turns"; R-ASK-05), then
/// runs the full guard pipeline (<see cref="GroundingGuard"/> + <see cref="NumericGuard"/>) and the
/// one-retry policy (<see cref="RegenerateOnce"/>) before ever returning a result a caller may
/// show or persist.
///
/// <para>
/// Never retrieves anything itself — <paramref name="pack"/> (see <see cref="AnswerAsync"/>) must
/// already be authorized, tenant-scoped context assembled by the composition root (ADR-011
/// "authorization before retrieval" — the same "operate on caller-supplied data" shape
/// <c>RagAnswerService</c> and <c>DeterministicQueryHandler</c> already use, generalized here from
/// an evidence list to a context pack).
/// </para>
/// </summary>
public sealed class AnswerComposer(IAiGateway aiGateway)
{
    // JsonStringEnumConverter: PackValue.Kind (PackValueKind) serializes as "Amount"/"Percentage"/
    // "Date"/"Number", never the numeric default — both a human/model reading the raw pack JSON
    // and Contigo.AiGateway.Fixtures.FixtureAiGateway's own pack-aware echo (which cannot
    // reference this project's PackValueKind type at all — ADR-002 dependency direction — and so
    // deserializes the same JSON into its own string-typed shape) need the readable form.
    private static readonly JsonSerializerOptions PackJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Answers <paramref name="question"/> against <paramref name="pack"/>. Pack size is not
    /// re-checked here — the composition root already ran it through
    /// <see cref="Pack.PackBudget.Apply"/> before calling this method.
    /// </summary>
    /// <param name="question">The user's question, in its own language (OQ-askv2-006).</param>
    /// <param name="pack">The already-assembled, already-budgeted context pack (R-ASK-04).</param>
    /// <param name="recentTurns">The conversation's last N turns (role, rendered markdown), oldest
    /// first — an empty list for a brand-new conversation.</param>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> or
    /// <paramref name="recentTurns"/> is <see langword="null"/>.</exception>
    public async Task<Result<AnswerComposerResult>> AnswerAsync(
        string question,
        IReadOnlyList<PackItem> pack,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(recentTurns);

        var packJson = JsonSerializer.Serialize(pack, PackJsonOptions);
        var promptedQuestion = BuildQuestionWithHistory(question, recentTurns);

        var first = await CallGatewayAsync(promptedQuestion, packJson, AnswerPromptV2.SystemPrompt, cancellationToken)
            .ConfigureAwait(false);
        if (first.IsFailure)
        {
            return Result<AnswerComposerResult>.Failure(first.Error);
        }

        var firstVerdict = Validate(first.Value, pack);
        if (firstVerdict.Passed)
        {
            return Result<AnswerComposerResult>.Success(
                new AnswerComposerResult(first.Value, GuardIntervened: false, GuardViolation: null));
        }

        // R-ASK-06: "regenerated once with the violation named".
        var retrySystemPrompt = AnswerPromptV2.SystemPrompt + Environment.NewLine + Environment.NewLine +
            RegenerateOnce.BuildRetryInstruction(firstVerdict.Violation!);

        var retry = await CallGatewayAsync(promptedQuestion, packJson, retrySystemPrompt, cancellationToken)
            .ConfigureAwait(false);

        if (retry.IsFailure)
        {
            var downgraded = RegenerateOnce.DowngradeToAbstain(first.Value.Metadata, pack, firstVerdict.Violation!);
            return Result<AnswerComposerResult>.Success(
                new AnswerComposerResult(downgraded, GuardIntervened: true, firstVerdict.Violation));
        }

        var retryVerdict = Validate(retry.Value, pack);
        if (retryVerdict.Passed)
        {
            return Result<AnswerComposerResult>.Success(
                new AnswerComposerResult(retry.Value, GuardIntervened: true, firstVerdict.Violation));
        }

        var downgradedAfterRetry = RegenerateOnce.DowngradeToAbstain(retry.Value.Metadata, pack, retryVerdict.Violation!);
        return Result<AnswerComposerResult>.Success(
            new AnswerComposerResult(downgradedAfterRetry, GuardIntervened: true, retryVerdict.Violation));
    }

    private Task<Result<AiAnswerResult>> CallGatewayAsync(
        string question, string packJson, string systemPrompt, CancellationToken cancellationToken) =>
        aiGateway.AnswerAsync(new AiAnswerRequest(question, Evidence: [], systemPrompt, packJson), cancellationToken);

    private static GuardVerdict Validate(AiAnswerResult result, IReadOnlyList<PackItem> pack)
    {
        var grounding = GroundingGuard.Validate(result, pack);
        return grounding.Passed ? NumericGuard.Validate(result.AnswerMarkdown, pack) : grounding;
    }

    private static string BuildQuestionWithHistory(
        string question, IReadOnlyList<(string Role, string Markdown)> recentTurns)
    {
        if (recentTurns.Count == 0)
        {
            return question;
        }

        var history = string.Join(
            Environment.NewLine, recentTurns.Select(turn => $"{turn.Role}: {turn.Markdown}"));

        return $"Conversation so far:{Environment.NewLine}{history}{Environment.NewLine}{Environment.NewLine}" +
            $"Current question: {question}";
    }
}
