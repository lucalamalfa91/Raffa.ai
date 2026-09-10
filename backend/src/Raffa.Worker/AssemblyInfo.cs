using System.Runtime.CompilerServices;

// Raffa.Worker.Tests builds a real HostApplicationBuilder via WorkerServiceCollectionExtensions
// (the public composition entry point) and then asserts against the internal Queue/* types --
// InMemoryQueueConsumer and QueueConsumerHostedService -- to prove the deployable-worker
// Definition of Done end-to-end, without promoting host-wiring internals to a public API surface
// (Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types forbids
// public non-Program/Startup/Extensions types on this host).
[assembly: InternalsVisibleTo("Raffa.Worker.Tests")]
