using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Implements task E23/F03/US01/T01 (phrase-edit-write, NW-63r; ADR-029 w18 footer clause 1):
/// <c>PATCH /api/contracts/{id}/evidence/{fieldName}</c> writes a reviewer's corrected OCR phrase
/// as an <b>override beside the proposal</b> on the same <see cref="ExtractionEvidence"/> row —
/// <see cref="ExtractionEvidence.Value"/> (what the model proposed) is never mutated;
/// <see cref="ExtractionEvidence.OverrideValue"/> (the column feature-02 added) is. Same
/// "validate everything before mutating, then write-then-audit" shape as
/// <see cref="ContractCorrectionService"/>/<see cref="NegotiationStepService"/>: owns its own
/// tenant scope (<see cref="ITenantContext.BeginScope"/>, ADR-009 belt-and-suspenders), and writes
/// one append-only <see cref="IAuditWriter"/> entry per successful <see cref="EditAsync"/> call.
///
/// <para>
/// <b>Which row the override lands on.</b> <c>StagedExtractionService</c>'s own write shape
/// (<c>dbContext.ExtractionEvidences.Add(...)</c> — always an insert, never an update) means a
/// field can carry more than one <see cref="ExtractionEvidence"/> row over its lifetime, newest
/// extraction winning for <em>display</em>
/// (<see cref="ContractEvidenceQueryService.GetLatestAsync"/>'s own
/// <c>OrderByDescending(CreatedAt)</c> grouping). This write targets exactly that same "latest row
/// for this field" — the one row the review pane currently shows as the proposal — the same
/// <c>rows.GroupBy(FieldName).Select(g => g.OrderByDescending(CreatedAt)...First())</c> shape
/// <c>DocumentValidationService.StampHumanAcceptedAsync</c> already uses to stamp
/// <see cref="ExtractionEvidence.Decision"/> onto that same row.
/// </para>
///
/// <para>
/// <b>Why a later reprocess cannot silently revert this write (ADR-027 fence, parent story AC-4).</b>
/// A reprocess inserts a brand-new row for the field with a later <c>CreatedAt</c> — this service
/// never has to special-case that, because <see cref="ContractEvidenceQueryService.GetLatestAsync"/>
/// reads <see cref="ExtractionEvidence.OverrideValue"/> independently of which row is "latest": the
/// most recent row that actually carries a non-null override, searched across every row for the
/// field, not just the newest one. The override therefore keeps surfacing on the read side even
/// after a reprocess's fresh proposal supersedes the row it was written on for display purposes —
/// "re-derivation never overrides a human correction" (ADR-027 w17 footer clause 2) applied to the
/// phrase-edit override the same way it already applies to a human-accepted <c>Decision</c>.
/// </para>
/// </summary>
public sealed class ContractPhraseEditService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter)
{
    /// <summary>Returned when no contract with the given id exists for the caller's tenant.
    /// <c>Raffa.Api.ContractsEndpointExtensions</c> maps exactly this string to 404, every other
    /// failure to 400 — the same sentinel-string convention
    /// <see cref="ContractCorrectionService.ContractNotFoundError"/>/
    /// <see cref="NegotiationStepService.ContractNotFoundError"/> already establish.</summary>
    public const string ContractNotFoundError = "Contract not found.";

    /// <summary>Returned when the requested field names no <see cref="ExtractionEvidence"/> row for
    /// this contract — there is nothing to write an override beside.
    /// <see cref="ExtractionEvidence.FieldName"/> is deliberately not a closed enum (that type's
    /// own doc comment), so this is discovered from the data, never from a fixed list.</summary>
    public static string EvidenceNotFoundError(string fieldName) =>
        $"No extraction evidence exists for field '{fieldName}' on this contract.";

    private const string AuditEditedAction = "contract.phrase_edited";
    private const string AuditResourceType = "contract";

    /// <param name="fieldName">The <see cref="ExtractionEvidence.FieldName"/> this phrase belongs
    /// to — the route's own <c>{fieldName}</c> segment, matched case-insensitively the same way
    /// <see cref="ContractEvidenceQueryService.GetLatestAsync"/>'s own grouping already is.</param>
    /// <param name="overrideValue">The reviewer's corrected phrase text. Blank is refused — clearing
    /// an override back to "none" is not a use case this endpoint serves (there is no product
    /// affordance for it); a reviewer who mis-typed it corrects it again with the right text.</param>
    /// <param name="actor">The caller's resolved token subject (ADR-011), recorded on the audit row
    /// — required, no default, so a placeholder can never return by omission (the same contract
    /// every other write in this module already keeps).</param>
    public async Task<Result<ContractPhraseEditResult>> EditAsync(
        TenantId tenantId,
        EntityId contractId,
        string fieldName,
        string? overrideValue,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (string.IsNullOrWhiteSpace(overrideValue))
        {
            return Result<ContractPhraseEditResult>.Failure(
                "'overrideValue' is required and cannot be empty.");
        }

        var trimmedOverride = overrideValue.Trim();

        // Entry point: open this call's own tenant scope before any query below (ADR-009) -- same
        // placement as every other write in this module.
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contractExists = await dbContext.Contracts
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (!contractExists)
        {
            return Result<ContractPhraseEditResult>.Failure(ContractNotFoundError);
        }

        // Tracked (no AsNoTracking): the row this call mutates must be attached to this DbContext's
        // change tracker for SaveChangesAsync to stage an UPDATE, not a second insert.
        var rows = await dbContext.ExtractionEvidences
            .Where(e => e.TenantId == tenantId && e.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var latest = rows
            .Where(e => string.Equals(e.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id.Value)
            .FirstOrDefault();

        if (latest is null)
        {
            return Result<ContractPhraseEditResult>.Failure(EvidenceNotFoundError(fieldName));
        }

        var now = clock.UtcNow;

        // The override, and only the override -- Value (the proposal), the box columns and every
        // other proposal-time fact on this row stay exactly what the model reported (AC-1; see
        // ExtractionEvidence.BoxX's own doc comment: "editing the phrase's text does not move the
        // box the model originally found it in").
        latest.OverrideValue = trimmedOverride;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recorded only once the write is durable, still inside this call's own tenant scope --
        // same placement as every other write in this module.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditEditedAction,
                AuditResourceType,
                contractId.Value.ToString(),
                now,
                $"fieldName={latest.FieldName}"),
            cancellationToken).ConfigureAwait(false);

        return Result<ContractPhraseEditResult>.Success(new ContractPhraseEditResult(
            contractId, latest.FieldName, latest.Value, latest.OverrideValue, now));
    }
}

/// <summary>Outcome of <see cref="ContractPhraseEditService.EditAsync"/>: the field's own proposal
/// (<see cref="Value"/>, unchanged) beside the override just written
/// (<see cref="OverrideValue"/>) — returned to the caller and echoed on
/// <c>PATCH /api/contracts/{id}/evidence/{fieldName}</c>'s <c>200</c> body so the same request that
/// wrote the override can render it without a second round trip.</summary>
public sealed record ContractPhraseEditResult(
    EntityId ContractId,
    string FieldName,
    string? Value,
    string? OverrideValue,
    DateTimeOffset EditedAt);

/// <summary>
/// <c>PATCH /api/contracts/{id}/evidence/{fieldName}</c> request body. Hand-written, not generated
/// — same reason as <see cref="ContractCorrectionRequest"/> (the generator does not parse
/// <c>requestBody</c> at all yet; <c>Raffa.ArchitectureTests.DependencyDirectionTests</c> only
/// inspects <c>Raffa.Api</c>/<c>Raffa.Worker</c>, so a request contract living there would be
/// flagged as business logic leaking into a host that must stay a thin composition root). Co-located
/// with <see cref="ContractPhraseEditService"/> rather than given its own file, the same way
/// <c>NegotiationStepsRequest</c> co-locates with <c>NegotiationStepService</c>.
/// </summary>
public sealed record ContractPhraseEditRequest(string? OverrideValue);
