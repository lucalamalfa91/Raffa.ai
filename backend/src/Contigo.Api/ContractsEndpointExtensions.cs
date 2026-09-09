using Contigo.Documents.Contracts.Application;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Suppliers;
using Contigo.SharedKernel.Tenancy;

namespace Contigo.Api;

/// <summary>
/// Maps `PATCH /api/contracts/{id}` (product spec Appendix A API table: "Validated field
/// corrections"; story us-01-correction-history AC-1, task E02/F05/US01/T01) and
/// `GET /api/contracts/{id}` (Appendix A: "Contract 360 data"; story
/// us-02-contract-360-aggregate AC-1/AC-2/AC-3, task E02/F03/US02/T01). Thin composition per
/// ADR-002 — <see cref="ContractCorrectionService"/> and <see cref="Contract360QueryService"/>
/// own the actual versioning/history and aggregation decisions respectively; this file only
/// translates HTTP &lt;-&gt; the service call, same shape as
/// <see cref="WorkspaceEndpointExtensions"/>/<see cref="AuditEndpointExtensions"/>/
/// <see cref="PortfolioEndpointExtensions"/>.
///
/// Same interim `X-Tenant-Id` header placeholder as <c>Program.cs</c>'s document endpoints,
/// <see cref="WorkspaceEndpointExtensions"/>, and <see cref="PortfolioEndpointExtensions"/>
/// (ADR-010 is not in either task's "Architecture decisions in force" list, so there is no
/// validated caller principal yet) — see <c>Program.cs</c>'s own comment on why this interim gap
/// is not promoted to reports/open-questions.md by these tasks.
/// corrections"; story us-01-correction-history AC-1, task E02/F05/US01/T01) and `GET
/// /api/contracts/{id}/corrections` (story us-01-correction-history AC-2 "correction history is
/// queryable", task E02/F05/US01/T02 — no dedicated Appendix A row exists for this sub-resource,
/// unlike e.g. `GET /api/quotes/{id}/assessment`'s own nested-route precedent). Thin composition
/// per ADR-002 — <see cref="ContractCorrectionService"/> / <see cref="ContractCorrectionHistoryQueryService"/>
/// own the actual versioning/history decisions; this file only translates HTTP &lt;-&gt; the
/// service call, same shape as <see cref="WorkspaceEndpointExtensions"/>/<see cref="AuditEndpointExtensions"/>.
///
/// Same interim `X-Tenant-Id` header placeholder as <c>Program.cs</c>'s document endpoints and
/// <see cref="WorkspaceEndpointExtensions"/> (ADR-010 is not in this task's "Architecture
/// decisions in force" list, so there is no validated caller principal yet) — see
/// <c>Program.cs</c>'s own comment on why this interim gap is not promoted to
/// reports/open-questions.md by this task. Deliberately not <see cref="AuditEndpointExtensions"/>'s
/// own <c>ClaimsPrincipal</c>/<c>WorkspacePrincipalAuthorization</c> shape: that endpoint already
/// has a validated-identity model this module does not, and mixing the two auth conventions inside
/// one file would be a worse inconsistency than the interim gap itself.
///
/// Task E03/F02/US01/T02 (renewal-alerts, parent story us-01-threshold-scheduler AC-3): `PATCH
/// /api/contracts/{id}` now also calls <see cref="RenewalAlertRecomputeService"/> immediately after
/// a successful correction — see that type's own doc comment for why this is the one place that
/// composition can happen (ADR-002) and exactly which corrected fields trigger it. The response
/// shape above is unchanged; the recompute is a side effect, not a new field this endpoint promises
/// to report (no AC/task text names a response shape for it).
///
/// <para>
/// Task E13/F03/US01/T02 (requirements R-SUP-04, ADR-024): the 360 header gains
/// <c>supplierName</c>, resolved through <see cref="ISupplierNameLookup"/> — the same "join the
/// name on here, in the one project allowed to reference every module" composition
/// <see cref="PortfolioEndpointExtensions"/> does for the portfolio row, and for the same ADR-002
/// reason. One id, so a one-entry batch rather than a second, single-id port. `PATCH
/// /api/contracts/{id}` needs no change of its own: <c>supplier</c> is simply another correctable
/// field name <see cref="ContractCorrectionService"/> accepts (R-SUP-03), and this endpoint has
/// always passed the caller's <c>corrections</c> map through untouched.
/// </para>
/// </summary>
public static class ContractsEndpointExtensions
{
    public static IEndpointRouteBuilder MapContractsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/contracts/{id}", GetContract360Async);
        endpoints.MapPatch("/api/contracts/{id}", CorrectContractAsync);
        endpoints.MapGet("/api/contracts/{id}/corrections", GetCorrectionHistoryAsync);
        endpoints.MapGet("/api/contracts/{id}/evidence", GetContractEvidenceAsync);
        return endpoints;
    }

    /// <summary>
    /// `GET /api/contracts/{id}/evidence`: the latest per-field extraction evidence for one
    /// contract (page, span, confidence, the quoted passage, the model) — the review screen's
    /// evidence pane and its per-field confidence tags read this. Same guard-clause shape as
    /// <see cref="GetCorrectionHistoryAsync"/>; 404 when <see cref="ContractEvidenceQueryService.GetLatestAsync"/>
    /// returns <c>null</c> (no such contract for this tenant), 200 with an empty array for a
    /// contract that exists but has no evidence yet.
    /// </summary>
    private static async Task<IResult> GetContractEvidenceAsync(
        string id,
        HttpRequest httpRequest,
        ContractEvidenceQueryService evidenceQueryService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var evidence = await evidenceQueryService.GetLatestAsync(
            new TenantId(tenantGuid), new EntityId(contractGuid), cancellationToken).ConfigureAwait(false);

        if (evidence is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(evidence.Select(e => new
        {
            fieldName = e.FieldName,
            value = e.Value,
            confidence = e.Confidence,
            sourcePage = e.SourcePage,
            sourceSpan = e.SourceSpan,
            sourceDocumentId = e.SourceDocumentId?.Value,
            sourceFileName = e.SourceFileName,
            passage = e.Passage,
            highlightStart = e.HighlightStart,
            highlightLength = e.HighlightLength,
            modelId = e.ModelId,
            extractedAt = e.ExtractedAt,
        }));
    }

    /// <summary>
    /// `GET /api/contracts/{id}` (us-02-contract-360-aggregate AC-1): the spec §8.2 header + tab
    /// aggregate. AC-3 "Authorization filter applies (default tenant scoping)": a contract that
    /// does not exist, or that belongs to a different tenant than the caller's `X-Tenant-Id`,
    /// both read back as 404 — <see cref="Contract360QueryService"/> cannot and does not
    /// distinguish the two (see that type's own doc comment on why, ADR-009).
    /// </summary>
    private static async Task<IResult> GetContract360Async(
        string id,
        HttpRequest request,
        Contract360QueryService contract360QueryService,
        ISupplierNameLookup supplierNameLookup,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var tenantId = new TenantId(tenantGuid);

        var result = await contract360QueryService
            .GetByIdAsync(tenantId, new EntityId(contractGuid), cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return Results.NotFound();
        }

        // One id, so a one-entry batch through the shared, tenant-scoped helper rather than a
        // second single-id port — see PortfolioEndpointExtensions.ResolveSupplierNamesAsync's own
        // doc comment on why the ambient tenant scope is not optional here.
        var supplierName = result.Header.SupplierId is { } supplierId
            ? (await PortfolioEndpointExtensions
                    .ResolveSupplierNamesAsync(tenantId, [supplierId], supplierNameLookup, tenantContext, cancellationToken)
                    .ConfigureAwait(false))
                .GetValueOrDefault(supplierId)
            : null;

        return Results.Ok(ToContract360Response(result, supplierName));
    }

    /// <summary>
    /// Wire-shapes <see cref="Contract360Result"/> in spec §8.2 tab order (Overview, Commercials,
    /// Products, Clauses, Obligations, Risks, Documents, Benchmark, Renewal, Activity). Enum
    /// members and <see cref="EntityId"/>/<see cref="EntityId"/>? wrapper values are projected to
    /// plain strings/GUIDs — the same convention <see cref="PortfolioEndpointExtensions"/> and
    /// <c>Program.cs</c>'s document endpoints already use, since neither has a custom JSON
    /// converter registered anywhere in this solution.
    /// </summary>
    private static object ToContract360Response(Contract360Result result, string? supplierName)
    {
        var header = result.Header;
        var overview = result.Overview;
        var commercials = result.Commercials;
        var renewal = result.Renewal;

        return new
        {
            contractId = result.ContractId.Value,
            header = new
            {
                contractId = header.ContractId.Value,
                supplierId = header.SupplierId?.Value,
                supplierName,
                type = header.Type.ToString(),
                status = header.Status,
                annualSpend = header.AnnualSpend,
                totalContractValue = header.TotalContractValue,
                startDate = header.StartDate,
                endDate = header.EndDate,
                renewalDate = header.RenewalDate,
                cancellationDeadline = header.CancellationDeadline,
                autoRenewal = header.AutoRenewal,
                risk = header.Risk?.ToString(),
            },
            tabs = new
            {
                overview = new
                {
                    currency = overview.Currency,
                    effectiveDate = overview.EffectiveDate,
                    renewalTermMonths = overview.RenewalTermMonths,
                    paymentTerms = overview.PaymentTerms,
                    governingLaw = overview.GoverningLaw,
                    parentContractId = overview.ParentContractId?.Value,
                    version = overview.Version,
                    createdAt = overview.CreatedAt,
                },
                commercials = new
                {
                    annualSpend = commercials.AnnualSpend,
                    totalContractValue = commercials.TotalContractValue,
                    currency = commercials.Currency,
                    paymentTerms = commercials.PaymentTerms,
                    autoRenewal = commercials.AutoRenewal,
                    renewalTermMonths = commercials.RenewalTermMonths,
                    lineItemCount = commercials.LineItemCount,
                    lineItemAnnualCostTotal = commercials.LineItemAnnualCostTotal,
                    lineItemTotalCostTotal = commercials.LineItemTotalCostTotal,
                },
                products = result.Products.Select(p => new
                {
                    lineItemId = p.LineItemId.Value,
                    productId = p.ProductId?.Value,
                    sku = p.Sku,
                    description = p.Description,
                    quantity = p.Quantity,
                    unit = p.Unit,
                    unitPrice = p.UnitPrice,
                    listPrice = p.ListPrice,
                    discount = p.Discount,
                    billingPeriod = p.BillingPeriod,
                    annualCost = p.AnnualCost,
                    totalCost = p.TotalCost,
                    sourceDocumentId = p.SourceDocumentId?.Value,
                    sourceSpan = p.SourceSpan,
                    sourcePage = p.SourcePage,
                    confidence = p.Confidence,
                }),
                clauses = result.Clauses.Select(c => new
                {
                    clauseId = c.ClauseId.Value,
                    clauseType = c.ClauseType,
                    rawText = c.RawText,
                    normalizedValue = c.NormalizedValue,
                    riskLevel = c.RiskLevel?.ToString(),
                    sourceDocumentId = c.SourceDocumentId?.Value,
                    sourceSpan = c.SourceSpan,
                    sourcePage = c.SourcePage,
                    confidence = c.Confidence,
                }),
                obligations = result.Obligations.Select(o => new
                {
                    obligationId = o.ObligationId.Value,
                    party = o.Party,
                    obligationType = o.ObligationType,
                    description = o.Description,
                    dueDate = o.DueDate,
                    recurrenceRule = o.RecurrenceRule,
                    criticality = o.Criticality,
                    status = o.Status,
                    sourceDocumentId = o.SourceDocumentId?.Value,
                    sourceSpan = o.SourceSpan,
                    sourcePage = o.SourcePage,
                    confidence = o.Confidence,
                }),
                risks = result.Risks.Select(r => new
                {
                    riskId = r.RiskId.Value,
                    riskType = r.RiskType,
                    severity = r.Severity.ToString(),
                    description = r.Description,
                    status = r.Status,
                    clauseId = r.ClauseId?.Value,
                    sourceDocumentId = r.SourceDocumentId?.Value,
                    sourceSpan = r.SourceSpan,
                    sourcePage = r.SourcePage,
                    confidence = r.Confidence,
                }),
                documents = result.Documents.Select(d => new
                {
                    documentId = d.DocumentId.Value,
                    fileName = d.FileName,
                    mimeType = d.MimeType,
                    documentType = d.DocumentType.ToString(),
                    processingStatus = d.ProcessingStatus.ToString(),
                    createdAt = d.CreatedAt,
                }),
                // Always empty in this wave — see Contract360Result's own doc comment
                // (us-02-contract-360-aggregate "Task-count note": benchmark/activity are R3/R4
                // placeholders that "read only validated data and return empty until later
                // waves").
                benchmark = Array.Empty<object>(),
                renewal = new
                {
                    endDate = renewal.EndDate,
                    renewalDate = renewal.RenewalDate,
                    cancellationDeadline = renewal.CancellationDeadline,
                    autoRenewal = renewal.AutoRenewal,
                    renewalTermMonths = renewal.RenewalTermMonths,
                },
                activity = Array.Empty<object>(),
            },
        };
    }

    private static async Task<IResult> CorrectContractAsync(
        string id,
        ContractCorrectionRequest request,
        HttpRequest httpRequest,
        ContractCorrectionService correctionService,
        RenewalAlertRecomputeService renewalAlertRecomputeService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        if (request.Corrections is null || request.Corrections.Count == 0)
        {
            return Results.BadRequest("At least one field correction in 'corrections' is required.");
        }

        var tenantId = new TenantId(tenantGuid);
        var contractId = new EntityId(contractGuid);

        var result = await correctionService.CorrectAsync(
            tenantId,
            contractId,
            request.Corrections,
            request.Reason,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return string.Equals(result.Error, ContractCorrectionService.ContractNotFoundError, StringComparison.Ordinal)
                ? Results.NotFound()
                : Results.BadRequest(result.Error);
        }

        var correction = result.Value;

        // Task E03/F02/US01/T02 (renewal-alerts, parent story us-01-threshold-scheduler AC-3):
        // recompute this contract's alerts only when a renewal-relevant field actually changed —
        // see RenewalAlertRecomputeService's own doc comment for exactly which fields those are and
        // why. Runs after the correction is already durable/audited, same placement as
        // ContractCorrectionService.CorrectAsync's own "write then audit" sequencing.
        if (correction.CorrectedFields.Any(RenewalAlertRecomputeService.RenewalRelevantFields.Contains))
        {
            await renewalAlertRecomputeService
                .RecomputeAsync(tenantId, contractId, cancellationToken)
                .ConfigureAwait(false);
        }

        return Results.Ok(new
        {
            contractId = correction.ContractId.Value,
            versionNumber = correction.VersionNumber,
            correctedFields = correction.CorrectedFields,
            correctedAt = correction.CorrectedAt,
        });
    }

    /// <summary>
    /// `GET /api/contracts/{id}/corrections` (task E02/F05/US01/T02, AC-2 "correction history is
    /// queryable"). Same guard-clause shape as <see cref="CorrectContractAsync"/> above; 404 when
    /// <see cref="ContractCorrectionHistoryQueryService.GetHistoryAsync"/> returns <c>null</c>
    /// (no such contract for this tenant) — a contract that exists but has never been corrected
    /// returns 200 with an empty array, not 404 (see that service's own doc comment).
    /// </summary>
    private static async Task<IResult> GetCorrectionHistoryAsync(
        string id,
        HttpRequest httpRequest,
        ContractCorrectionHistoryQueryService historyQueryService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var history = await historyQueryService.GetHistoryAsync(
            new TenantId(tenantGuid), new EntityId(contractGuid), cancellationToken).ConfigureAwait(false);

        if (history is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(history.Select(entry => new
        {
            fieldName = entry.FieldName,
            previousValue = entry.PreviousValue,
            newValue = entry.NewValue,
            correctedBy = entry.CorrectedBy,
            correctedAt = entry.CorrectedAt,
            reason = entry.Reason,
        }));
    }
}
