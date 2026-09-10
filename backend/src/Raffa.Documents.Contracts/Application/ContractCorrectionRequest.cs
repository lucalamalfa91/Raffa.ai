namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Request body for `PATCH /api/contracts/{id}` (task E02/F05/US01/T01, us-01-correction-history
/// AC-1: "PATCH /api/contracts/{id} records a correction as a new version"). Deliberately living
/// here rather than in <c>Raffa.Api</c> — the same reason
/// <c>Raffa.Identity.Workspace.Infrastructure.CreateWorkspaceRequest</c> lives next to
/// <c>WorkspaceProvisioningService</c> instead of the host (see that type's own doc comment):
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>
/// only inspects the <c>Raffa.Api</c>/<c>Raffa.Worker</c> assemblies, so a request contract
/// living there would be flagged as business logic leaking into a host that must stay a thin
/// composition root.
///
/// <see cref="Corrections"/> is a field-name/new-value string map rather than one strongly-typed
/// property per <see cref="Domain.Contract"/> field: <see cref="Domain.CorrectionHistory"/> itself
/// stores every correction as a string-valued (<c>PreviousValue</c>/<c>NewValue</c>) diff (see
/// that type's doc comment), so the wire shape mirrors the storage shape exactly — one canonical
/// per-field string parser (<see cref="ContractCorrectionService"/>) instead of two (one for JSON
/// binding, one for the history row). A key absent from the map leaves that field untouched; a key
/// present with a JSON <c>null</c> clears an optional field (rejected for a field that is
/// required — see <see cref="ContractCorrectionService"/>'s field table). Only
/// <see cref="ContractCorrectionService.CorrectableFieldNames"/> are accepted.
/// </summary>
public sealed record ContractCorrectionRequest(
    Dictionary<string, string?>? Corrections,
    string? Reason);
