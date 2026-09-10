using System.Globalization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace Raffa.AiEval.TenantFixtures;

/// <summary>
/// Pins "now" for the whole golden-set run (<see cref="AiEvalOptions.EvaluationInstant"/>), so
/// every relative-date expectation in <c>golden/*.json</c> is compared against the same instant
/// the fixture data was authored from — never a wall clock a midnight rollover could flake
/// against.
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

/// <summary>
/// <see cref="IAiGateway"/> decorator that counts every call made during one golden case, then
/// delegates unchanged. R-ASK-01/R-ASK-02 ("off-domain turns never retrieve") is asserted at its
/// strongest through this: a greeting/off-domain/legal/capability turn must record <b>zero</b>
/// gateway calls of any kind, which is strictly stronger than "zero retrieval calls" because
/// <c>EmbeddingRetrievalService.SearchAsync</c>'s own unconditional first step is
/// <see cref="EmbedAsync"/>.
///
/// <para>
/// <see cref="Reset"/> is called by the runner before each case, so the count belongs to exactly
/// one question. Cases run sequentially by construction (one <c>await</c> at a time in
/// <c>GoldenSetRunner</c>), so no locking is needed; the counter is nonetheless kept as a simple
/// list append rather than a shared mutable dictionary to keep that obvious.
/// </para>
/// </summary>
internal sealed class RecordingAiGateway(IAiGateway inner) : IAiGateway
{
    private readonly List<string> _calls = [];

    /// <summary>Every method name invoked since the last <see cref="Reset"/>, in call order.</summary>
    public IReadOnlyList<string> Calls => _calls;

    public void Reset() => _calls.Clear();

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

/// <summary>
/// Captures the single per-turn audit row <c>Raffa.Api.AskCopilotService.WriteAuditAsync</c>
/// writes (R-ASK-09). That row is the only place the engine reports whether its own guard pipeline
/// intervened — <c>abstainGuardIntervened=true|false</c> in
/// <see cref="AuditEntry.Detail"/> — which is exactly R-EVD-03's headline assertion
/// ("hallucination (numeric guard interventions) must be 0 on the golden set"). Reading it here,
/// rather than reconstructing it from the reply kind, is what lets the set tell a guard catch apart
/// from an honest empty-pack abstain: both return <c>abstain</c>.
/// </summary>
internal sealed class RecordingAuditWriter : IAuditWriter
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public void Reset() => _entries.Clear();

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Runs <c>DocumentsContractsDbContext.OnModelCreating</c> unchanged, then re-maps
/// <see cref="Embedding.Vector"/> through a plain string value converter so the EF Core InMemory
/// provider can carry the column at all. <c>Pgvector.Vector</c> is a CLR class the InMemory
/// provider has no mapping for, and <c>EmbeddingConfiguration</c>'s own
/// <c>HasColumnType("vector(1536)")</c> is meaningless outside Npgsql, so without this the context
/// fails model validation the moment any of its DbSets is touched.
///
/// <para>
/// <b>Why a converter and not <c>Ignore</c></b> (which is what
/// <c>Raffa.Api.Tests.TestSupport.InMemoryModelCustomizer</c> does): the golden set really does
/// reach <c>EmbeddingRetrievalService.SearchAsync</c> — the <c>clause</c> intent is part of the
/// required coverage. An <em>ignored</em> property makes that service's own
/// <c>OrderBy(e =&gt; e.Vector.CosineDistance(query))</c> fail to compile as a query at all
/// ("Translation of member 'Vector' ... failed. This commonly occurs when the specified member is
/// unmapped"), which surfaces as an HTTP 500 rather than as a reply. Mapped through a converter,
/// the same query is client-evaluated against an empty <c>Embeddings</c> set: the ordering lambda
/// is never invoked (there are no rows to order), so <c>CosineDistance</c> — which has no
/// client-side implementation — is never called, and the service returns an empty hit list. That
/// is exactly the "authorized retrieval genuinely found nothing" branch the clause cases assert on
/// (R-ASK-07's honest abstain). The real pgvector similarity ranking stays
/// <c>Raffa.IntegrationTests</c>' job against a real Postgres.
/// </para>
/// </summary>
internal sealed class InMemoryModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    /// <summary>Round-trips a vector as its comma-separated components. "R" round-trip formatting
    /// keeps the conversion lossless, so a future case that does index a chunk still reads back
    /// exactly what it wrote.</summary>
    private static readonly ValueConverter<Vector, string> VectorAsCsv = new(
        vector => string.Join(',', vector.ToArray().Select(component => component.ToString("R", CultureInfo.InvariantCulture))),
        text => new Vector(Parse(text)));

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        if (modelBuilder.Model.FindEntityType(typeof(Embedding)) is not null)
        {
            modelBuilder.Entity<Embedding>().Property(e => e.Vector).HasConversion(VectorAsCsv);
        }
    }

    private static float[] Parse(string text) => text.Length == 0
        ? []
        : text.Split(',').Select(part => float.Parse(part, CultureInfo.InvariantCulture)).ToArray();
}
