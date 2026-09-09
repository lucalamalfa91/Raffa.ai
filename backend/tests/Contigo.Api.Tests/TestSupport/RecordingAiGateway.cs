using Contigo.AiGateway;
using Contigo.AiGateway.Contracts;
using Contigo.SharedKernel;

namespace Contigo.Api.Tests.TestSupport;

/// <summary>
/// <see cref="IAiGateway"/> decorator that records the name of every method invoked on it, then
/// delegates unchanged to <paramref name="inner"/> — task E13/F06/US01/T01's own Definition of
/// Done line for <c>Contigo.Api.Tests</c>: "zero retrieval calls on 'ciao' (recording retrieval
/// fake)". Same "wrap IAiGateway, observe, delegate" shape
/// <c>Contigo.IntegrationTests.AskContigoRagCrossTenantIsolationTests.GuardViolatingAiGateway</c>
/// already uses to intercept one call; this one only ever observes.
///
/// <para>
/// Retrieval itself (<c>Contigo.Documents.Contracts.Application.EmbeddingRetrievalService
/// .SearchAsync</c>) is a sealed, DB-backed concrete class, not an interface — there is no DI seam
/// to substitute a recording fake directly at that layer without changing
/// <c>Contigo.Api.AskCopilotService</c>'s own constructor shape (out of this task's file scope).
/// <see cref="EmbedAsync"/> is that method's own unconditional first step (its doc comment: "Embeds
/// <c>queryText</c> via <c>IAiGateway.EmbedAsync</c>... AC-2") before it ever touches the database,
/// so "no <see cref="EmbedAsync"/> call recorded" already proves "no retrieval call happened" —
/// recording every method (not just that one) additionally proves the stronger claim the greeting
/// gate label's own doc comment makes: zero AI Gateway calls of <em>any</em> kind
/// (<c>Contigo.Chat.Application.Reply.RedirectReplyBuilder.GreetingOrOffDomain</c>'s own doc
/// comment: "No Contigo.AiGateway call happens for any of these").
/// </para>
/// </summary>
internal sealed class RecordingAiGateway(IAiGateway inner) : IAiGateway
{
    private readonly List<string> _calls = [];

    /// <summary>Every method name invoked on this instance, in call order (e.g. "EmbedAsync",
    /// "AnswerAsync").</summary>
    public IReadOnlyList<string> Calls => _calls;

    public Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(ClassifyAsync));
        return inner.ClassifyAsync(request, cancellationToken);
    }

    public Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(ExtractAsync));
        return inner.ExtractAsync(request, cancellationToken);
    }

    public Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(EmbedAsync));
        return inner.EmbedAsync(request, cancellationToken);
    }

    public Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(AnswerAsync));
        return inner.AnswerAsync(request, cancellationToken);
    }

    public Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default)
    {
        _calls.Add(nameof(OcrAsync));
        return inner.OcrAsync(request, cancellationToken);
    }
}
