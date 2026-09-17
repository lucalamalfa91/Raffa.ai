using Raffa.Api.Infrastructure;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

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
///
/// <para>
/// Task E19/F03/US01/T01 (NW-13, story us-01-step-ticks-api; ADR-028 §D3): `GET`/`PUT
/// /api/contracts/{id}/negotiation-steps` — Contract 360's negotiation tracker "4-step checklist"
/// ticks, now server-side instead of the retired <c>sessionStorage</c> store. Same guard-clause
/// shape as <see cref="GetCorrectionHistoryAsync"/> below (<see cref="ICallerContext.ResolveTenantAsync"/>
/// first, non-GUID id 400, unknown contract 404); the whole-set semantics themselves belong to
/// <see cref="NegotiationStepService.SetAsync"/>. No host change needed: <c>MapContractsEndpoints</c>
/// is already called by the API host and <see cref="NegotiationStepService"/> is already registered
/// through <c>AddDocumentsContractsModule</c>, so both routes arrive with zero edits to
/// <c>Program.cs</c> — ADR-028 reserved that file for this task; this task finds it does not need
/// it.
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
        endpoints.MapGet("/api/contracts/{id}/negotiation-steps", GetNegotiationStepsAsync);
        endpoints.MapPut("/api/contracts/{id}/negotiation-steps", PutNegotiationStepsAsync);
        return endpoints;
    }

    /// <summary>
    /// `GET /api/contracts/{id}/evidence`: the latest per-field extraction evidence for one
    /// contract (page, span, confidence, decision, the geometry box, the quoted passage, the
    /// model) — the review screen's evidence pane and its per-field confidence tags read this,
    /// and (epic-23 feature-04) the viewer draws <c>box</c> over the cited phrase. The server's
    /// <c>autoAcceptThreshold</c> is emitted once per response so the web never hardcodes 90.
    /// Same guard-clause shape as <see cref="GetCorrectionHistoryAsync"/>;
    /// 404 when <see cref="ContractEvidenceQueryService.GetLatestAsync"/> returns <c>null</c>
    /// (no such contract for this tenant), 200 with an empty <c>fields</c> array for a contract
    /// that exists but has no evidence yet.
    /// </summary>
    private static async Task<IResult> GetContractEvidenceAsync(
        string id,
        HttpRequest httpRequest,
        ContractEvidenceQueryService evidenceQueryService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

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

        return Results.Ok(new
        {
            autoAcceptThreshold = ExtractionConfidencePolicy.AutoAcceptThreshold,
            fields = evidence.Select(e => new
            {
                fieldName = e.FieldName,
                value = e.Value,
                confidence = e.Confidence,
                decision = e.Decision,
                sourcePage = e.SourcePage,
                sourceSpan = e.SourceSpan,
                box = e.Box is { } box
                    ? new { x = box.X, y = box.Y, width = box.Width, height = box.Height }
                    : null,
                sourceDocumentId = e.SourceDocumentId?.Value,
                sourceFileName = e.SourceFileName,
                passage = e.Passage,
                highlightStart = e.HighlightStart,
                highlightLength = e.HighlightLength,
                modelId = e.ModelId,
                extractedAt = e.ExtractedAt,
            }),
        });
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
        DocumentQueryService documentQueryService,
        ISupplierNameLookup supplierNameLookup,
        ITenantContext tenantContext,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

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

        // ADR-027 §D9: one top-level answer to "can this contract be shown yet?", computed over
        // the documents linked to the contract rather than inferred by the screen from the
        // (possibly still empty) tab tree.
        var readiness = await documentQueryService
            .GetReadinessAsync(tenantId, new EntityId(contractGuid), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(ToContract360Response(result, supplierName, readiness));
    }

    /// <summary>
    /// Wire-shapes <see cref="Contract360Result"/> in spec §8.2 tab order (Overview, Commercials,
    /// Products, Clauses, Obligations, Risks, Documents, Benchmark, Renewal, Activity). Enum
    /// members and <see cref="EntityId"/>/<see cref="EntityId"/>? wrapper values are projected to
    /// plain strings/GUIDs — the same convention <see cref="PortfolioEndpointExtensions"/> and
    /// <c>Program.cs</c>'s document endpoints already use, since neither has a custom JSON
    /// converter registered anywhere in this solution. <paramref name="readiness"/> rides at the
    /// top level next to <c>header</c> (ADR-027 §D9); its <c>stage</c> is nullable on purpose
    /// and therefore carries no enum on the wire (ADR-012 w15 §9).
    /// </summary>
    private static object ToContract360Response(
        Contract360Result result,
        string? supplierName,
        ContractReadiness readiness)
    {
        var header = result.Header;
        var overview = result.Overview;
        var commercials = result.Commercials;
        var renewal = result.Renewal;

        return new
        {
            contractId = result.ContractId.Value,
            readiness = new
            {
                state = readiness.StateApiValue,
                stage = readiness.Stage,
                documentCount = readiness.DocumentCount,
                completedDocumentCount = readiness.CompletedDocumentCount,
            },
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
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        if (request.Decision is not null)
        {
            return Results.BadRequest("decision is server-computed and cannot be supplied by the caller.");
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
            caller.Identity!,
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
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

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

    /// <summary>
    /// `GET /api/contracts/{id}/negotiation-steps` (task E19/F03/US01/T01, NW-13; parent story
    /// us-01-step-ticks-api AC-1). Same guard-clause shape as <see cref="GetCorrectionHistoryAsync"/>
    /// above; 404 when <see cref="NegotiationStepService.GetAsync"/> returns <c>null</c> (no such
    /// contract for this tenant) — a contract that exists but has ticked nothing yet returns 200
    /// with an empty array, not 404.
    /// </summary>
    private static async Task<IResult> GetNegotiationStepsAsync(
        string id,
        HttpRequest httpRequest,
        NegotiationStepService negotiationStepService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var steps = await negotiationStepService.GetAsync(
            new TenantId(tenantGuid), new EntityId(contractGuid), cancellationToken).ConfigureAwait(false);

        if (steps is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(steps);
    }

    /// <summary>
    /// `PUT /api/contracts/{id}/negotiation-steps` (task E19/F03/US01/T01, NW-13; parent story
    /// us-01-step-ticks-api AC-2/AC-3/AC-6). Same guard-clause shape as
    /// <see cref="GetNegotiationStepsAsync"/> above; an unknown step name or an unknown contract
    /// both fail before anything is written — see <see cref="NegotiationStepService.SetAsync"/>'s
    /// own doc comment. <paramref name="request"/> is bound from the JSON body separately from
    /// <paramref name="httpRequest"/>, which this handler uses only for
    /// <see cref="ICallerContext.ResolveTenantAsync"/> — the same headers-vs-body split
    /// <see cref="CorrectContractAsync"/> above already uses.
    /// </summary>
    private static async Task<IResult> PutNegotiationStepsAsync(
        string id,
        NegotiationStepsRequest request,
        HttpRequest httpRequest,
        NegotiationStepService negotiationStepService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        if (request.Steps is null)
        {
            return Results.BadRequest("'steps' is required.");
        }

        // caller.Identity is guaranteed non-null once caller.Failure is null (CallerTenantResult's
        // own doc comment) -- this is the validated token subject, recorded as the audit actor
        // (NegotiationStepService.SetAsync's own doc comment on why this task needs no placeholder).
        var result = await negotiationStepService.SetAsync(
            new TenantId(tenantGuid),
            new EntityId(contractGuid),
            request.Steps,
            caller.Identity!,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return string.Equals(result.Error, NegotiationStepService.ContractNotFoundError, StringComparison.Ordinal)
                ? Results.NotFound()
                : Results.BadRequest(result.Error);
        }

        return Results.Ok(result.Value);
    }
}
