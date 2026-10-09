using System.Runtime.CompilerServices;

// Raffa.AiFlows.Tests exercises the flows' internal guards, parsers and orchestrators directly;
// Raffa.Api.Tests keeps asserting on the internal seams the Ask flow exposes once it moves here
// (the same grant Raffa.Api/AssemblyInfo.cs gives it today for the types it hosts).
// Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types filters on
// Type.IsPublic and is not affected by these grants.
[assembly: InternalsVisibleTo("Raffa.AiFlows.Tests")]
[assembly: InternalsVisibleTo("Raffa.Api.Tests")]
