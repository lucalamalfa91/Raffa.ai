using Raffa.SharedKernel;

namespace Raffa.Chat.Application;

/// <summary>
/// The minimal snapshot of one validated <c>Raffa.Documents.Contracts.Domain.Contract</c> that
/// <see cref="DeterministicQueryHandler"/> needs to answer a
/// <see cref="Raffa.Chat.Domain.DeterministicQueryKind.RenewalWindow"/> or
/// <see cref="Raffa.Chat.Domain.DeterministicQueryKind.AnnualSpend"/> question (spec §8.3).
///
/// <para>
/// This intentionally duplicates a subset of
/// <c>Raffa.Documents.Contracts.Application.PortfolioListItem</c>'s shape rather than
/// referencing that type: ADR-002's dependency-direction rule forbids <c>Raffa.Chat</c> from
/// referencing <c>Raffa.Documents.Contracts</c> at all
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module is
/// exactly <c>[SharedKernel, AiGateway]</c> — see <c>backend/README.md</c>'s "Dependency
/// direction" table). Mapping a real <c>Contract</c> row to this DTO is the composition root's
/// job (<c>Raffa.Api</c>, which may reference every module — ADR-002 "API Host -&gt; all
/// modules"), the same place <c>PortfolioQueryService</c> itself is already composed from; no
/// task in this wave wires that composition yet (task E02/F04/US01/T02 depends only on
/// <c>query-router</c> in the wave-spec DAG, not on a Documents/Contracts capability). Until that
/// wiring lands, a caller of <see cref="DeterministicQueryHandler"/> supplies an in-memory list
/// however it likes — a test's fixture data today, a real query result mapped 1:1 later.
/// </para>
/// </summary>
/// <param name="ContractId">The source <c>Contract.Id</c>.</param>
/// <param name="SupplierId">The source <c>Contract.SupplierId</c> — a nullable cross-module
/// reference by id only, same as the source column (Suppliers/Products owns the Supplier
/// aggregate and is still an empty scaffold; no supplier name is available anywhere yet).</param>
/// <param name="AnnualSpend">The source <c>Contract.AnnualSpend</c>. A null value is excluded from
/// an <see cref="Raffa.Chat.Domain.DeterministicQueryKind.AnnualSpend"/> sum rather than treated
/// as zero, so a missing/unvalidated figure never silently understates the total.</param>
/// <param name="EndDate">The source <c>Contract.EndDate</c> — the effective renewal/expiry date a
/// <see cref="Raffa.Chat.Domain.DeterministicQueryKind.RenewalWindow"/> query filters on.</param>
/// <param name="AutoRenewal">The source <c>Contract.AutoRenewal</c>. A contract only "renews" (as
/// opposed to merely "ends") when this is true — the same rule
/// <c>Raffa.Documents.Contracts.Application.PortfolioListItem.RenewalDate</c> already
/// documents.</param>
public sealed record ContractFact(
    EntityId ContractId,
    EntityId? SupplierId,
    decimal? AnnualSpend,
    DateOnly? EndDate,
    bool AutoRenewal);
