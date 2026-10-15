using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>
/// F5-T05 (PR-3): the stage scheduler. With every option at its default the seven gateway requests and the
/// call timing are exactly the pipeline's historic ones; each switch (concurrency cap, context
/// dependencies, in-run transient retry) is proved with a scripted gateway, no real model.
/// </summary>
public sealed class StageSchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private const string Text = "[[PAGE 1]]\nabc\n\n[[PAGE 2]]\ndef\n\n";

    private static readonly IReadOnlyList<DocumentPageText> Pages =
    [
        new DocumentPageText(1, "abc"),
        new DocumentPageText(2, "def"),
    ];

    private static readonly ExtractionStage[] ListStages =
        [ExtractionStage.LineItems, ExtractionStage.LegalClauses, ExtractionStage.Obligations, ExtractionStage.Risk];

    private const string MetadataPayload =
        """{"facts":[{"field":"currency","value":"EUR","sourcePage":1,"sourceSpan":"EUR","confidence":0.95},{"field":"governingLaw","value":"Italian law","sourcePage":1,"sourceSpan":"x","confidence":0.5},{"field":"status","value":"active","sourcePage":1,"sourceSpan":"x","confidence":0.99}]}""";

    private const string DatesPayload =
        """{"facts":[{"field":"startDate","value":"2026-01-01","sourcePage":1,"sourceSpan":"1 Jan 2026","confidence":0.97}]}""";

    private sealed class AdvancingClock : IClock
    {
        private long _ticks;

        public DateTimeOffset UtcNow => Start.AddSeconds(Interlocked.Increment(ref _ticks));
    }

    /// <summary>Scripted gateway: records requests in call order, tracks concurrency, can hold a call open,
    /// fail a stage a number of times and answer a per-stage payload.</summary>
    private sealed class ScriptedGateway : IAiGateway
    {
        private readonly object _lock = new();
        private int _inFlight;
        private int _maxConcurrent;

        public List<AiExtractionRequest> Requests { get; } = [];

        /// <summary>Stage names whose call had completed when each request arrived (by request index).</summary>
        public List<string[]> CompletedAtStart { get; } = [];

        public List<string> Completed { get; } = [];

        public Dictionary<string, string> Payloads { get; } = new();

        public Dictionary<string, int> TransientFailuresLeft { get; } = new();

        public HashSet<string> PermanentFailures { get; } = [];

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        /// <summary>When set, every call waits until this many calls are in flight at once (proves overlap).</summary>
        public int? WaitForInFlight { get; set; }

        private readonly TaskCompletionSource _overlap = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrent => _maxConcurrent;

        public int CallsOf(ExtractionStage stage) =>
            Requests.Count(r => r.StageName == stage.ToString());

        public async Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            int inFlight;
            lock (_lock)
            {
                Requests.Add(request);
                CompletedAtStart.Add(Completed.ToArray());
                inFlight = ++_inFlight;
                _maxConcurrent = Math.Max(_maxConcurrent, inFlight);
                if (WaitForInFlight is { } target && inFlight >= target)
                {
                    _overlap.TrySetResult();
                }
            }

            if (WaitForInFlight is not null)
            {
                await _overlap.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }

            Result<AiExtractionResult> result;
            lock (_lock)
            {
                _inFlight--;
                if (PermanentFailures.Contains(request.StageName))
                {
                    result = Result<AiExtractionResult>.Failure("Simulated rejected request.");
                }
                else if (TransientFailuresLeft.TryGetValue(request.StageName, out var left) && left > 0)
                {
                    TransientFailuresLeft[request.StageName] = left - 1;
                    result = Result<AiExtractionResult>.Failure($"{AiGatewayErrors.UnavailablePrefix} simulated 429.");
                }
                else
                {
                    var payload = Payloads.TryGetValue(request.StageName, out var json) ? json : "{}";
                    result = Result<AiExtractionResult>.Success(new AiExtractionResult(
                        payload, new AiCallMetadata("test-model", "1", "test-v1", Start, "hash-" + request.StageName)));
                }

                Completed.Add(request.StageName);
            }

            return result;
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Harness
    {
        private readonly string _database = Guid.NewGuid().ToString("N");
        private readonly AdvancingClock _clock = new();
        private readonly TenantContext _tenantContext = new();

        public TenantId TenantId { get; } = TenantId.New();

        public EntityId DocumentId { get; private set; }

        public async Task SeedAsync()
        {
            await using var db = InMemoryDocumentsDb.Create(_database);
            var document = new Document
            {
                TenantId = TenantId,
                FileName = "contract.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{TenantId.Value:D}/documents/contract.pdf",
                Checksum = "checksum-1",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Start,
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            DocumentId = document.Id;
        }

        public async Task<Result<StagedExtractionSummary>> RunAsync(
            ScriptedGateway gateway, ExtractionOrchestrationOptions? options)
        {
            await using var db = InMemoryDocumentsDb.Create(_database);
            var orchestrator = new ExtractionOrchestrator(
                new ExtractionRunStore(db, _tenantContext, _clock, new RecordingAuditWriter()),
                gateway,
                ExtractionStages.Default,
                _tenantContext,
                options);
            return await orchestrator.RunAsync(TenantId, DocumentId, Pages);
        }

        public async Task<List<ExtractionJob>> JobsAsync()
        {
            await using var db = InMemoryDocumentsDb.Create(_database);
            return await db.ExtractionJobs.AsNoTracking()
                .Where(j => j.Stage != ExtractionStage.Classification)
                .OrderBy(j => j.QueuedAt)
                .ToListAsync();
        }
    }

    private static async Task<Harness> NewHarnessAsync()
    {
        var harness = new Harness();
        await harness.SeedAsync();
        return harness;
    }

    private static string ExpectedSchema(string stageName) => stageName switch
    {
        "Metadata" => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.Metadata),
        "CommercialTerms" => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.CommercialTerms),
        "DatesAndRenewalTerms" => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.DatesAndRenewalTerms),
        "LineItems" => StagedExtractionJsonSchemas.LineItems(),
        "LegalClauses" => StagedExtractionJsonSchemas.Clauses(),
        "Obligations" => StagedExtractionJsonSchemas.Obligations(),
        "Risk" => StagedExtractionJsonSchemas.Risks(),
        _ => throw new ArgumentOutOfRangeException(nameof(stageName)),
    };

    [Fact]
    public void The_options_default_to_todays_behaviour()
    {
        var options = new ExtractionOrchestrationOptions();

        Assert.Equal(0, options.MaxConcurrentStages);
        Assert.False(options.UseContextDependencies);
        Assert.Equal(0, options.TransientRetriesPerStage);
    }

    [Fact]
    public void Only_the_four_list_stages_declare_the_three_scalar_stages_as_dependencies()
    {
        foreach (var stage in ExtractionStages.Default)
        {
            if (ListStages.Contains(stage.Stage))
            {
                Assert.Equal(
                    [ExtractionStage.Metadata, ExtractionStage.CommercialTerms, ExtractionStage.DatesAndRenewalTerms],
                    stage.DependsOn);
            }
            else
            {
                Assert.Empty(stage.DependsOn);
            }
        }
    }

    [Fact]
    public async Task With_every_option_at_its_default_the_seven_requests_are_byte_equal_and_all_in_flight_at_once()
    {
        // Same scenario with no options object and with an explicit all-default one: identical requests,
        // all seven calls started before any of them returns (NW-106), job hash = SHA256("staged-v1\n"+text).
        foreach (var options in new ExtractionOrchestrationOptions?[] { null, new ExtractionOrchestrationOptions() })
        {
            var harness = await NewHarnessAsync();
            var gateway = new ScriptedGateway { WaitForInFlight = 7 };
            gateway.Payloads["Metadata"] = MetadataPayload;
            gateway.Payloads["DatesAndRenewalTerms"] = DatesPayload;

            var result = await harness.RunAsync(gateway, options);

            Assert.True(result.IsSuccess);
            Assert.Equal(7, gateway.Requests.Count);
            Assert.Equal(7, gateway.MaxConcurrent);
            Assert.Equal(ExtractionPipeline.Stages.Select(s => s.ToString()), gateway.Requests.Select(r => r.StageName));
            Assert.All(gateway.Requests, r =>
                Assert.Equal(new AiExtractionRequest(r.StageName, Text, ExpectedSchema(r.StageName)), r));
            Assert.All(gateway.CompletedAtStart, completed => Assert.Empty(completed));

            var jobs = await harness.JobsAsync();
            Assert.Equal(7, jobs.Count);
            Assert.All(jobs, job => Assert.Equal(ExtractionInputHash.Compute(Text), job.InputHash));
        }
    }

    [Fact]
    public async Task Options_that_are_switched_off_never_change_the_requests_even_when_the_numbers_are_set()
    {
        // MaxConcurrentStages 0 and retries 0 are the defaults; the context flag alone gates the block.
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway();
        gateway.Payloads["Metadata"] = MetadataPayload;

        var result = await harness.RunAsync(gateway, new ExtractionOrchestrationOptions { UseContextDependencies = false });

        Assert.True(result.IsSuccess);
        Assert.All(gateway.Requests, r => Assert.Equal(Text, r.DocumentText));
        Assert.DoesNotContain(gateway.Requests, r => r.DocumentText.Contains("[[CONTEXT]]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_a_cap_of_two_no_more_than_two_stage_calls_are_in_flight()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway { Delay = TimeSpan.FromMilliseconds(60) };

        var result = await harness.RunAsync(gateway, new ExtractionOrchestrationOptions { MaxConcurrentStages = 2 });

        Assert.True(result.IsSuccess);
        Assert.Equal(7, gateway.Requests.Count);
        Assert.Equal(2, gateway.MaxConcurrent);
        Assert.All(gateway.Requests, r => Assert.Equal(Text, r.DocumentText));
        // The first slots go to the first stages in pipeline order; later hand-offs race on the pool.
        Assert.Equal(["Metadata", "CommercialTerms"], gateway.Requests.Take(2).Select(r => r.StageName));
        Assert.Equal(ExtractionPipeline.Stages.Select(s => s.ToString()).Order(), gateway.Requests.Select(r => r.StageName).Order());
    }

    [Fact]
    public async Task With_context_dependencies_list_stages_start_after_the_scalar_stages_and_receive_their_facts()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway { Delay = TimeSpan.FromMilliseconds(40) };
        gateway.Payloads["Metadata"] = MetadataPayload;
        gateway.Payloads["DatesAndRenewalTerms"] = DatesPayload;

        var result = await harness.RunAsync(gateway, new ExtractionOrchestrationOptions { UseContextDependencies = true });

        Assert.True(result.IsSuccess);
        Assert.Equal(7, gateway.Requests.Count);

        const string block = "[[CONTEXT]]\ncurrency: EUR\nstartDate: 2026-01-01\n[[/CONTEXT]]\n";
        var scalarNames = new[] { "Metadata", "CommercialTerms", "DatesAndRenewalTerms" };
        for (var i = 0; i < gateway.Requests.Count; i++)
        {
            var request = gateway.Requests[i];
            if (scalarNames.Contains(request.StageName))
            {
                Assert.Equal(Text, request.DocumentText);
                Assert.Empty(gateway.CompletedAtStart[i]);
            }
            else
            {
                // low-confidence governingLaw and the lifecycle status are not in the block
                Assert.Equal(block + Text, request.DocumentText);
                Assert.Equal(ExpectedSchema(request.StageName), request.JsonSchema);
                Assert.Equal(scalarNames.Order(), gateway.CompletedAtStart[i].Order());
            }
        }

        // Q2: the hash of the effective request text per stage; scalar stages keep today's hash.
        var jobs = await harness.JobsAsync();
        Assert.All(jobs.Where(j => scalarNames.Contains(j.Stage.ToString())), j => Assert.Equal(ExtractionInputHash.Compute(Text), j.InputHash));
        Assert.All(jobs.Where(j => ListStages.Contains(j.Stage)), j => Assert.Equal(ExtractionInputHash.Compute(block + Text), j.InputHash));
    }

    [Fact]
    public async Task A_failed_scalar_stage_does_not_stop_a_dependent_list_stage()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway();
        gateway.PermanentFailures.Add("Metadata");
        gateway.Payloads["DatesAndRenewalTerms"] = DatesPayload;

        var result = await harness.RunAsync(gateway, new ExtractionOrchestrationOptions { UseContextDependencies = true });

        Assert.True(result.IsSuccess);
        Assert.Equal(7, gateway.Requests.Count);
        var jobs = await harness.JobsAsync();
        Assert.Equal(ExtractionJobStatus.Failed, jobs.Single(j => j.Stage == ExtractionStage.Metadata).Status);
        Assert.All(jobs.Where(j => ListStages.Contains(j.Stage)), j => Assert.NotEqual(ExtractionJobStatus.Failed, j.Status));
        foreach (var request in gateway.Requests.Where(r => ListStages.Select(s => s.ToString()).Contains(r.StageName)))
        {
            Assert.Equal("[[CONTEXT]]\nstartDate: 2026-01-01\n[[/CONTEXT]]\n" + Text, request.DocumentText);
        }
    }

    [Fact]
    public async Task A_malformed_scalar_payload_gives_no_context_and_does_not_stop_a_dependent_list_stage()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway();
        gateway.Payloads["Metadata"] = "{not json";

        var result = await harness.RunAsync(gateway, new ExtractionOrchestrationOptions { UseContextDependencies = true });

        Assert.True(result.IsSuccess);
        Assert.All(
            gateway.Requests.Where(r => ListStages.Select(s => s.ToString()).Contains(r.StageName)),
            r => Assert.Equal(Text, r.DocumentText));
        var jobs = await harness.JobsAsync();
        Assert.Equal(ExtractionJobStatus.Failed, jobs.Single(j => j.Stage == ExtractionStage.Metadata).Status);
        Assert.All(jobs, j => Assert.Equal(ExtractionInputHash.Compute(Text), j.InputHash));
    }

    [Fact]
    public async Task Context_dependencies_with_a_cap_do_not_deadlock()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway { Delay = TimeSpan.FromMilliseconds(20) };
        gateway.Payloads["Metadata"] = MetadataPayload;

        var run = harness.RunAsync(
            gateway, new ExtractionOrchestrationOptions { UseContextDependencies = true, MaxConcurrentStages = 1 });
        var result = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, gateway.Requests.Count);
        Assert.Equal(1, gateway.MaxConcurrent);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_in_run_at_most_N_times_then_recorded_transient()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway();
        gateway.TransientFailuresLeft["Risk"] = 2;
        gateway.TransientFailuresLeft["Obligations"] = 5;

        var result = await harness.RunAsync(gateway, new ExtractionOrchestrationOptions { TransientRetriesPerStage = 2 });

        Assert.True(result.IsSuccess);
        Assert.Equal(3, gateway.CallsOf(ExtractionStage.Risk));
        Assert.Equal(3, gateway.CallsOf(ExtractionStage.Obligations));
        Assert.Equal(1, gateway.CallsOf(ExtractionStage.Metadata));
        var jobs = await harness.JobsAsync();
        Assert.NotEqual(ExtractionJobStatus.Failed, jobs.Single(j => j.Stage == ExtractionStage.Risk).Status);
        var obligations = jobs.Single(j => j.Stage == ExtractionStage.Obligations);
        Assert.Equal(ExtractionJobStatus.Failed, obligations.Status);
        Assert.Equal(ExtractionStageFailureKind.Transient, obligations.FailureKind);
    }

    [Fact]
    public async Task With_the_default_of_zero_retries_a_transient_failure_is_called_once()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway();
        gateway.TransientFailuresLeft["Risk"] = 1;

        var result = await harness.RunAsync(gateway, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, gateway.CallsOf(ExtractionStage.Risk));
        var risk = (await harness.JobsAsync()).Single(j => j.Stage == ExtractionStage.Risk);
        Assert.Equal(ExtractionStageFailureKind.Transient, risk.FailureKind);
    }

    [Fact]
    public async Task A_permanent_failure_is_never_retried_in_run()
    {
        var harness = await NewHarnessAsync();
        var gateway = new ScriptedGateway();
        gateway.PermanentFailures.Add("Risk");

        var result = await harness.RunAsync(gateway, new ExtractionOrchestrationOptions { TransientRetriesPerStage = 3 });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, gateway.CallsOf(ExtractionStage.Risk));
        var risk = (await harness.JobsAsync()).Single(j => j.Stage == ExtractionStage.Risk);
        Assert.Equal(ExtractionStageFailureKind.Permanent, risk.FailureKind);
    }

    [Fact]
    public async Task A_resumed_run_with_dependencies_rebuilds_the_context_from_the_contract_and_reruns_the_list_stages()
    {
        var harness = await NewHarnessAsync();
        var on = new ExtractionOrchestrationOptions { UseContextDependencies = true };

        var first = new ScriptedGateway();
        first.Payloads["Metadata"] = MetadataPayload;
        first.Payloads["DatesAndRenewalTerms"] = DatesPayload;
        first.PermanentFailures.Add("Risk");
        Assert.True((await harness.RunAsync(first, on)).IsSuccess);

        var second = new ScriptedGateway();
        second.Payloads["Metadata"] = MetadataPayload;
        Assert.True((await harness.RunAsync(second, on)).IsSuccess);

        // Scalar checkpoints are reused (no call); the list stages are never reused under dependencies.
        Assert.Equal(
            ListStages.Select(s => s.ToString()).Order(),
            second.Requests.Select(r => r.StageName).Order());
        Assert.All(second.Requests, r =>
            Assert.Equal("[[CONTEXT]]\ncurrency: EUR\nstartDate: 2026-01-01\n[[/CONTEXT]]\n" + Text, r.DocumentText));
    }

    [Fact]
    public async Task A_resume_never_mixes_modes_in_either_direction()
    {
        // ON run with a failed list stage, resumed OFF: the finished list stages carry a context hash, so
        // they are not reused (only the scalar checkpoints are) and the requests are the plain text.
        var harness = await NewHarnessAsync();
        var first = new ScriptedGateway();
        first.Payloads["Metadata"] = MetadataPayload;
        first.PermanentFailures.Add("Risk");
        Assert.True((await harness.RunAsync(first, new ExtractionOrchestrationOptions { UseContextDependencies = true })).IsSuccess);

        var off = new ScriptedGateway();
        Assert.True((await harness.RunAsync(off, null)).IsSuccess);
        Assert.Equal(ListStages.Select(s => s.ToString()).Order(), off.Requests.Select(r => r.StageName).Order());
        Assert.All(off.Requests, r => Assert.Equal(Text, r.DocumentText));

        // OFF run with a failed list stage, resumed ON: the OFF list checkpoints are never reused either.
        var other = await NewHarnessAsync();
        var offFirst = new ScriptedGateway();
        offFirst.PermanentFailures.Add("Risk");
        Assert.True((await other.RunAsync(offFirst, null)).IsSuccess);

        var on = new ScriptedGateway();
        Assert.True((await other.RunAsync(on, new ExtractionOrchestrationOptions { UseContextDependencies = true })).IsSuccess);
        Assert.Equal(ListStages.Select(s => s.ToString()).Order(), on.Requests.Select(r => r.StageName).Order());
    }
}
