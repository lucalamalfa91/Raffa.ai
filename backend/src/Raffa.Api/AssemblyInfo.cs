using System.Runtime.CompilerServices;

// Task E14/F02/US02/T01 (wave w14, story us-02-admin-role-from-membership): WorkspaceRoleResolverTests
// asserts Raffa.Api.Infrastructure.WorkspaceRoleResolver's resolution order (claims -> membership,
// ADR-025 §E) directly -- against an EF Core InMemory IdentityWorkspaceDbContext and the real
// HeaderCallerIdentity, not the whole ASP.NET host -- without promoting the resolver, ICallerIdentity
// or HeaderCallerIdentity to a public API surface on this composition root. Same shape
// Raffa.Worker/AssemblyInfo.cs already uses for Raffa.Worker.Tests: Raffa.ArchitectureTests.
// DependencyDirectionTests.Host_must_not_contain_domain_types filters on Type.IsPublic, which this
// grant does not change.
[assembly: InternalsVisibleTo("Raffa.Api.Tests")]
