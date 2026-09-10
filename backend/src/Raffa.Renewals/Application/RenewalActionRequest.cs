using Raffa.Renewals.Domain;

namespace Raffa.Renewals.Application;

/// <summary>
/// Request body for `POST /api/renewals/{id}/action` (task E03/F03/US01/T02, renewal-action;
/// parent story us-01-renewal-dashboard-api AC-3). Deliberately living here rather than in
/// `Raffa.Api` — the same reason
/// `Raffa.Documents.Contracts.Application.ContractCorrectionRequest` lives next to
/// `ContractCorrectionService` instead of the host (see that type's own doc comment):
/// `Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types` only
/// inspects the `Raffa.Api`/`Raffa.Worker` assemblies, so a request contract living there
/// would be flagged as business logic leaking into a host that must stay a thin composition root.
///
/// All three fields are the raw, unvalidated wire values — <see cref="Status"/> is a plain string,
/// not <see cref="RenewalActionStatus"/>: <see cref="RenewalActionService.SetActionAsync"/> is the
/// one place that parses it against that enum, so an invalid value fails with the same
/// <c>Result&lt;T&gt;.Failure</c> shape as an empty <see cref="Owner"/>/<see cref="Action"/>, not a
/// framework-level 400 with a less specific message.
/// </summary>
public sealed record RenewalActionRequest(string? Owner, string? Status, string? Action);
