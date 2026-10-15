using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Raffa.Messaging;
using Raffa.SharedKernel.Jobs;

namespace Raffa.Worker.Tests;

/// <summary>
/// Task F6-T05 (shared job kernel): one transport for several job kinds, no entity merge. Proves the
/// publisher selection (same predicate as the document queue), the settlement policy for a kernel job
/// (identical to the document pipeline's) and the consumer-side routing, all without a broker.
/// </summary>
public sealed class JobKernelTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JobRequested Message(string kind = JobKinds.QuoteExtraction, int schema = JobRequested.CurrentSchemaVersion) =>
        new(kind, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), schema);

    private static byte[] Body(JobRequested message) => JsonSerializer.SerializeToUtf8Bytes(message, Web);

    private sealed class RecordingHandler(string kind, JobHandleOutcome outcome = JobHandleOutcome.Handled, Exception? throws = null)
        : IJobHandler
    {
        public string Kind { get; } = kind;

        public List<JobRequested> Seen { get; } = [];

        public List<long> DeliveryCounts { get; } = [];

        public Task<JobHandleOutcome> HandleAsync(
            JobRequested message, long deliveryCount, CancellationToken cancellationToken = default)
        {
            Seen.Add(message);
            DeliveryCounts.Add(deliveryCount);
            return throws is null ? Task.FromResult(outcome) : Task.FromException<JobHandleOutcome>(throws);
        }
    }

    private static JobDeliveryProcessor Processor(params IJobHandler[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        return new JobDeliveryProcessor(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobDeliveryProcessor>.Instance);
    }

    // ----- transport selection -----

    [Fact]
    public void No_namespace_gives_a_non_durable_publisher_and_no_service_bus_job_publisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddExtractionQueuePublisher(new ConfigurationBuilder().AddInMemoryCollection().Build());

        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IJobQueuePublisher>();
        Assert.IsType<InlineJobQueuePublisher>(publisher);
        Assert.False(publisher.IsDurable);
    }

    [Fact]
    public void A_namespace_selects_the_service_bus_job_publisher_with_the_same_predicate_as_documents()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceBus:FullyQualifiedNamespace"] = "sbns-raffa-dev.servicebus.windows.net",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddExtractionQueuePublisher(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobQueuePublisher) && d.ImplementationType == typeof(ServiceBusJobQueuePublisher));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(InlineJobQueuePublisher));
    }

    [Fact]
    public void The_consumer_registers_the_job_processor_in_both_transports()
    {
        foreach (var settings in new[]
        {
            new Dictionary<string, string?>(),
            new Dictionary<string, string?> { ["ServiceBus:FullyQualifiedNamespace"] = "sbns.servicebus.windows.net" },
        })
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddExtractionQueueConsumer(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            Assert.Contains(services, d => d.ServiceType == typeof(JobDeliveryProcessor));
            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        }
    }

    [Fact]
    public async Task The_inline_publisher_records_but_delivers_nothing()
    {
        var publisher = new InlineJobQueuePublisher();
        var message = Message();

        await publisher.PublishAsync(message);

        Assert.Same(message, Assert.Single(publisher.Published));
        Assert.False(publisher.IsDurable);
    }

    // ----- settlement -----

    [Theory]
    [InlineData(JobHandleOutcome.Handled, 1)]
    [InlineData(JobHandleOutcome.Handled, 9)]
    [InlineData(JobHandleOutcome.ClaimLost, 1)]
    [InlineData(JobHandleOutcome.ClaimLost, 4)]
    public void A_present_row_completes(JobHandleOutcome outcome, long deliveryCount)
    {
        Assert.Equal(ExtractionSettlement.Action.Complete, ExtractionSettlement.Decide(outcome, deliveryCount));
    }

    [Theory]
    [InlineData(0, ExtractionSettlement.Action.Abandon)]
    [InlineData(1, ExtractionSettlement.Action.Abandon)]
    [InlineData(2, ExtractionSettlement.Action.DeadLetter)]
    [InlineData(7, ExtractionSettlement.Action.DeadLetter)]
    public void An_absent_row_abandons_below_two_deliveries_and_dead_letters_from_two(
        long deliveryCount, ExtractionSettlement.Action expected)
    {
        Assert.Equal(expected, ExtractionSettlement.Decide(JobHandleOutcome.JobNotFound, deliveryCount));
    }

    // ----- routing -----

    [Fact]
    public async Task A_message_is_routed_to_the_handler_that_owns_its_kind_only()
    {
        var quotes = new RecordingHandler(JobKinds.QuoteExtraction);
        var other = new RecordingHandler("something-else");
        var message = Message();

        var decision = await Processor(other, quotes).ProcessAsync(Body(message), 1, CancellationToken.None);

        Assert.Equal(ExtractionSettlement.Action.Complete, decision.Action);
        Assert.Equal(message, Assert.Single(quotes.Seen));
        Assert.Empty(other.Seen);
    }

    [Fact]
    public async Task The_handler_is_told_how_many_times_the_broker_delivered_the_message()
    {
        var handler = new RecordingHandler(JobKinds.QuoteExtraction);

        await Processor(handler).ProcessAsync(Body(Message()), 3, CancellationToken.None);

        Assert.Equal(3, Assert.Single(handler.DeliveryCounts));
    }

    [Fact]
    public async Task A_missing_row_is_abandoned_once_then_dead_lettered_as_job_not_found()
    {
        var handler = new RecordingHandler(JobKinds.QuoteExtraction, JobHandleOutcome.JobNotFound);
        var processor = Processor(handler);
        var body = Body(Message());

        var first = await processor.ProcessAsync(body, 1, CancellationToken.None);
        var second = await processor.ProcessAsync(body, 2, CancellationToken.None);

        Assert.Equal(ExtractionSettlement.Action.Abandon, first.Action);
        Assert.Equal(ExtractionSettlement.Action.DeadLetter, second.Action);
        Assert.Equal(ExtractionSettlement.JobNotFoundReason, second.Reason);
    }

    [Fact]
    public async Task A_lost_claim_is_completed_not_abandoned()
    {
        var handler = new RecordingHandler(JobKinds.QuoteExtraction, JobHandleOutcome.ClaimLost);

        var decision = await Processor(handler).ProcessAsync(Body(Message()), 3, CancellationToken.None);

        Assert.Equal(ExtractionSettlement.Action.Complete, decision.Action);
    }

    [Fact]
    public async Task A_handler_exception_abandons_for_redelivery()
    {
        var handler = new RecordingHandler(JobKinds.QuoteExtraction, throws: new InvalidOperationException("boom"));

        var decision = await Processor(handler).ProcessAsync(Body(Message()), 1, CancellationToken.None);

        Assert.Equal(ExtractionSettlement.Action.Abandon, decision.Action);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_into_a_settlement()
    {
        var handler = new RecordingHandler(JobKinds.QuoteExtraction, throws: new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Processor(handler).ProcessAsync(Body(Message()), 1, CancellationToken.None));
    }

    [Fact]
    public async Task A_kind_no_handler_owns_is_dead_lettered()
    {
        var decision = await Processor(new RecordingHandler("other"))
            .ProcessAsync(Body(Message()), 1, CancellationToken.None);

        Assert.Equal(ExtractionSettlement.Action.DeadLetter, decision.Action);
        Assert.Equal("unknown-job-kind", decision.Reason);
    }

    [Fact]
    public async Task An_unsupported_schema_version_is_dead_lettered_without_calling_the_handler()
    {
        var handler = new RecordingHandler(JobKinds.QuoteExtraction);

        var decision = await Processor(handler).ProcessAsync(Body(Message(schema: 99)), 1, CancellationToken.None);

        Assert.Equal(ExtractionSettlement.Action.DeadLetter, decision.Action);
        Assert.Equal("unsupported-schema", decision.Reason);
        Assert.Empty(handler.Seen);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"kind\":\"\",\"schemaVersion\":1}")]
    public async Task A_body_that_cannot_be_a_job_is_dead_lettered_as_malformed(string body)
    {
        var decision = await Processor(new RecordingHandler(JobKinds.QuoteExtraction))
            .ProcessAsync(System.Text.Encoding.UTF8.GetBytes(body), 1, CancellationToken.None);

        Assert.Equal(ExtractionSettlement.Action.DeadLetter, decision.Action);
        Assert.Equal("malformed", decision.Reason);
    }

    [Fact]
    public void The_wire_format_is_camel_case_json_with_the_documented_fields()
    {
        var message = new JobRequested(JobKinds.QuoteExtraction, Guid.Empty, Guid.Empty, Guid.Empty, 1);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(message, Web));

        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["kind", "tenantId", "resourceId", "jobId", "schemaVersion"], names);
        Assert.Equal("quote-extraction", JobKinds.QuoteExtraction);
        Assert.Equal("JobRequested", JobRequested.MessageSubject);
    }
}
