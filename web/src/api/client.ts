// Thin, hand-written HTTP glue around the generated OpenAPI types
// (./generated/schema.ts, itself generated from
// web/openapi/contigo-api.v1.json by web/scripts/generate-api-client.mjs).
// ADR-012 / api-consumption.md #1 forbid hand-written *divergent DTOs* -- the
// response shapes below (`HealthBody`, `CreateWorkspaceBody`) are anchored to
// the generated `paths` type, not invented here; only the fetch() plumbing is
// hand-written, the same way src/config/appConfig.ts hand-writes its own
// fetch() call around a runtime-validated shape.
//
// Task E01/F07/US01/T02 ("Generate TS API client from OpenAPI; wire
// /health"): this module *is* that client, and getHealth() is the /health
// wiring the parent story's Definition of Done exercises ("curl on /health
// via the API client succeeds") -- see src/App.tsx for where it is called.
//
// Task E06/F03/US01/T01 (signin-workspace-picker, AC-1 "Create a new
// workspace"): added createWorkspace(), the first write call this client
// makes. The request body type is hand-written (`CreateWorkspaceRequest`),
// not generated: web/scripts/generate-api-client.mjs does not parse
// `requestBody` at all yet (only `responses`), and the shape is a single
// required string field -- not worth extending the generator for until a
// second operation needs a typed request body too. See
// web/src/routes/signin/workspaceStore.ts for why there is no matching
// listWorkspaces()/getWorkspaces() call here: no such backend endpoint
// exists yet.
import type { paths } from "./generated/schema";

type HealthResponses = paths["/health"]["get"]["responses"];
type HealthBody =
  | HealthResponses[200]["content"]["text/plain"]
  | HealthResponses[503]["content"]["text/plain"];

export interface HealthCheckResult {
  /**
   * True only for a completed HTTP request with a 2xx status (Healthy or
   * Degraded, per the default `HealthCheckOptions` backend/src/Contigo.Api
   * /Program.cs's `app.MapHealthChecks("/health")` uses).
   */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** Response body text (the health status name), or -- when statusCode is null -- a description of the failure. */
  body: HealthBody | string;
}

type CreateWorkspaceResponses = paths["/api/workspaces"]["post"]["responses"];
type CreateWorkspaceBody = CreateWorkspaceResponses[201]["content"]["application/json"];

/** `POST /api/workspaces` request body (backend/.../WorkspaceEndpointExtensions.cs's `CreateWorkspaceRequest`). Hand-written -- see this file's header comment for why. */
export interface CreateWorkspaceRequest {
  name: string;
}

export interface CreateWorkspaceResult {
  /** True only on `201 Created`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The created workspace, present only when `ok` is true. */
  workspace: CreateWorkspaceBody | null;
  /** Plain-language failure reason (400 validation message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E06/F04/US01/T01 (workspace-members-invite): `inviteWorkspaceMember`, wrapping
// `POST /api/workspaces/{tenantId}/invites`. The 201 body is anchored to the generated
// `paths["/api/workspaces/{tenantId}/invites"]["post"]` type -- not invented. There is still no
// `GET /api/workspaces/{id}/members` (see src/routes/signin/workspaceStore.ts); the members table
// therefore seeds the current Admin locally and appends each successful invite from this call.
type InviteWorkspaceMemberResponses = paths["/api/workspaces/{tenantId}/invites"]["post"]["responses"];
export type InvitedMemberBody = InviteWorkspaceMemberResponses[201]["content"]["application/json"];
export type InviteWorkspaceRole = InvitedMemberBody["role"];

/** `POST /api/workspaces/{tenantId}/invites` request body. Hand-written -- see this file's header comment for why (the generator does not parse `requestBody`). */
export interface InviteWorkspaceMemberRequest {
  email: string;
  role: InviteWorkspaceRole;
}

export interface InviteWorkspaceMemberResult {
  /** True only on `201 Created`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The created membership, present only when `ok` is true. */
  member: InvitedMemberBody | null;
  /** Plain-language failure reason (400 validation message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E06/F05/US01/T01 (document-upload, AC-1/AC-2/AC-3): `uploadDocument`,
// the client's second write call. `UploadedDocument` is anchored to the
// generated `paths["/api/documents"]["post"]` 201 body -- not hand-invented
// -- the same discipline `CreateWorkspaceBody` above follows.
// `DocumentProcessingStatus` is exported (not just used inline) so
// src/routes/documents/uploadPipeline.ts derives its outcome mapping from
// this one contract-sourced union instead of redeclaring it.
type UploadDocumentResponses = paths["/api/documents"]["post"]["responses"];
export type UploadedDocument = UploadDocumentResponses[201]["content"]["application/json"];
export type DocumentProcessingStatus = UploadedDocument["processingStatus"];

export interface UploadDocumentResult {
  /** True only on `201 Created`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The stored document (already processed -- see `ApiClient.uploadDocument`'s own doc comment), present only when `ok` is true. */
  document: UploadedDocument | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E06/F05/US02/T01 (document-status-readback, screen 3's *other* half
// per ADR-020 "screen 3 may be two"): `getDocument`, wrapping the
// `GET /api/documents/{id}` operation the README already flagged as
// "generated in src/api/generated/schema.ts but has no client.ts wrapper
// yet ... belongs to whichever future task builds ... the document table /
// status read-back" -- that task is this one. `ReadBackDocument` carries
// `documentType`, which `UploadedDocument` (the POST response) does not --
// see src/routes/documents/documentStore.ts for why the document table
// re-fetches this instead of only trusting the upload response.
type GetDocumentResponses = paths["/api/documents/{id}"]["get"]["responses"];
export type ReadBackDocument = GetDocumentResponses[200]["content"]["application/json"];
export type DocumentType = ReadBackDocument["documentType"];

export interface GetDocumentResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The document, present only when `ok` is true. */
  document: ReadBackDocument | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E07/F01/US01/T01 (us-01-portfolio-list-filters, epic-07-web-contract-intelligence): `getPortfolio`,
// wrapping `GET /api/contracts` (backend/src/Contigo.Api/PortfolioEndpointExtensions.cs). This is the first
// web epic to reach into the set of E02-E05 backend routes this file's own header comment and
// openapi/contigo-api.v1.json's info.description both named as deliberately not yet added.
//
// `risk` is typed by the generated schema as a bare `string | null` (see openapi/contigo-api.v1.json's
// `getPortfolio` operation description for exactly why -- generate-api-client.mjs#renderSchemaType drops a
// nullable union's `null` member whenever the schema also declares `enum`, and every enum this document
// declared before this task was non-nullable, so that combination never came up). `PortfolioRiskSeverity`
// below names the real four-value wire set for this app's own code (filters, semantic-tag mapping) without
// pretending the generated type already knows it; `isPortfolioRiskSeverity` is the one runtime check where a
// raw `string | null` value actually crosses into that narrower type, so an unexpected fifth value from a
// future backend change fails safe (treated as "unknown", never mis-tagged) instead of silently miscompiling.
type GetPortfolioResponses = paths["/api/contracts"]["get"]["responses"];
export type PortfolioPageBody = GetPortfolioResponses[200]["content"]["application/json"];
export type PortfolioListItem = PortfolioPageBody["items"][number];
export type PortfolioContractType = PortfolioListItem["type"];
export type PortfolioRiskSeverity = "Low" | "Medium" | "High" | "Critical";

const PORTFOLIO_RISK_SEVERITIES: readonly PortfolioRiskSeverity[] = ["Low", "Medium", "High", "Critical"];

export function isPortfolioRiskSeverity(value: string): value is PortfolioRiskSeverity {
  return (PORTFOLIO_RISK_SEVERITIES as readonly string[]).includes(value);
}

/**
 * `GET /api/contracts` query parameters, named and typed 1:1 against
 * `PortfolioEndpointExtensions.TryParseFilter`/`TryParsePage` (backend). Every member is optional --
 * an absent one is not sent at all, matching the endpoint's own "absent = not filtered" contract
 * (`PortfolioFilter.None`/`PortfolioPageRequest.Default`). Hand-written, like `CreateWorkspaceRequest`
 * above: the generator does not parse an operation's `parameters` (only `responses`), and this is a
 * flat set of primitives, not worth extending it for (see this file's own header comment).
 */
export interface PortfolioQueryParams {
  supplierId?: string;
  status?: string;
  risk?: PortfolioRiskSeverity;
  autoRenewal?: boolean;
  minAnnualSpend?: number;
  maxAnnualSpend?: number;
  /** `yyyy-MM-dd`, matching the backend's `DateOnly` parameter. */
  renewalFrom?: string;
  /** `yyyy-MM-dd`, matching the backend's `DateOnly` parameter. */
  renewalTo?: string;
  /** 1-based; omit for page 1. */
  page?: number;
  /** Omit for the backend's own default (25); `PortfolioPageRequest.MaxPageSize` caps it at 100. */
  pageSize?: number;
}

export interface GetPortfolioResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The requested page (`items` + paging metadata), present only when `ok` is true. */
  portfolio: PortfolioPageBody | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E07/F02/US01/T01 (contract-360, ADR-020 screen 5): getContract360, wrapping
// `GET /api/contracts/{id}` -- the header + 10-tab aggregate. Second web epic to extend
// openapi/contigo-api.v1.json beyond the R0/health set (task E07/F01/US01/T01, portfolio, was the
// first) -- see that operation's own `description` in the OpenAPI document for the full provenance
// and for why `header.risk`/`tabs.clauses[].riskLevel` are bare nullable strings, not enums (the
// same generator limitation `PortfolioRiskSeverity` below already works around).
type GetContract360Responses = paths["/api/contracts/{id}"]["get"]["responses"];
export type Contract360Body = GetContract360Responses[200]["content"]["application/json"];
export type Contract360HeaderBody = Contract360Body["header"];
export type Contract360TabsBody = Contract360Body["tabs"];
export type Contract360ProductBody = Contract360TabsBody["products"][number];
export type Contract360ClauseBody = Contract360TabsBody["clauses"][number];
export type Contract360ObligationBody = Contract360TabsBody["obligations"][number];
export type Contract360RiskBody = Contract360TabsBody["risks"][number];
export type Contract360DocumentBody = Contract360TabsBody["documents"][number];
export type Contract360RenewalBody = Contract360TabsBody["renewal"];

export interface GetContract360Result {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The full header + 10-tab aggregate, present only when `ok` is true. */
  contract: Contract360Body | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E07/F02/US01/T01: getRenewals, wrapping `GET /api/renewals` -- the only source of the real,
// deterministic recommendedAction/explanation text the Overview tab's recommendation block renders
// (Contigo.Renewals is not reachable from GET /api/contracts/{id} at all; see the OpenAPI
// operation's own description for why this call, not invented UI copy, is the honest source).
type GetRenewalsResponses = paths["/api/renewals"]["get"]["responses"];
export type RenewalsPageBody = GetRenewalsResponses[200]["content"]["application/json"];
export type RenewalPipelineItemBody = RenewalsPageBody["items"][number];

export interface GetRenewalsResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The tenant's whole renewal pipeline (auto-renewing contracts only), present only when `ok` is true. */
  renewals: RenewalsPageBody | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E07/F02/US01/T01: getRenewalPriority, wrapping `GET /api/renewals/{contractId}/priority` --
// the Contract 360 header's "priority {score}/100" fact and the Renewal tab's priority-score
// component table (ADR-020 screen 5).
type GetRenewalPriorityResponses = paths["/api/renewals/{contractId}/priority"]["get"]["responses"];
export type RenewalPriorityBody = GetRenewalPriorityResponses[200]["content"]["application/json"];

export interface GetRenewalPriorityResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The score breakdown, present only when `ok` is true. */
  priority: RenewalPriorityBody | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E07/F03/US01/T01 (field-review-correction, ADR-020 screen 6): getCorrectionHistory, wrapping
// `GET /api/contracts/{id}/corrections` -- the real, newest-first per-field correction trail
// (ContractCorrectionHistoryQueryService). A field with at least one entry here has already been
// durably corrected by a human; src/routes/contracts/review/reviewViewModel.ts#buildReviewFields
// combines this with getContract360's current values to decide each field's review state. Third web
// epic to extend openapi/contigo-api.v1.json beyond the R0/portfolio/contract-360 set (see that
// file's own "repeating chore" provenance paragraph).
type GetCorrectionHistoryResponses = paths["/api/contracts/{id}/corrections"]["get"]["responses"];
export type CorrectionHistoryPageBody = GetCorrectionHistoryResponses[200]["content"]["application/json"];
export type CorrectionHistoryEntryBody = CorrectionHistoryPageBody[number];

export interface GetCorrectionHistoryResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** Newest-first correction history (possibly empty -- a contract that has never been corrected is still `ok: true`), present only when `ok` is true. */
  history: CorrectionHistoryPageBody | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E07/F03/US01/T01: correctContract, wrapping `PATCH /api/contracts/{id}` -- the one real
// write path this screen's "Correct" decision calls (ContractCorrectionService). Same never-throws
// shape as every other call here: a `400` (unknown field, unparsable value for its field's type, or
// a no-op correction ContractCorrectionService rejects outright) is a normal, expected outcome the
// caller renders inline, not an exception.
type CorrectContractResponses = paths["/api/contracts/{id}"]["patch"]["responses"];
export type ContractCorrectionBody = CorrectContractResponses[200]["content"]["application/json"];

/**
 * `PATCH /api/contracts/{id}` request body. Hand-written, not generated -- see this file's header
 * comment for why (the generator does not parse `requestBody` at all yet). `corrections` keys must
 * be one of `ContractCorrectionService.CorrectableFieldNames`
 * (backend/src/Contigo.Documents.Contracts/Application/ContractCorrectionService.cs); values are
 * that field's own canonical wire string (dates `yyyy-MM-dd`, booleans `"true"`/`"false"`,
 * decimals/integers via plain `toString()`), or `null` to clear an optional field.
 * `src/routes/contracts/review/reviewViewModel.ts#CORRECTABLE_FIELDS` mirrors that same field/kind
 * table on the client side.
 */
export interface CorrectContractRequest {
  corrections: Record<string, string | null>;
  reason?: string | null;
}

export interface CorrectContractResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The resulting version/correctedFields summary, present only when `ok` is true. */
  correction: ContractCorrectionBody | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E08/F01/US01/T01 (renewal-pipeline, ADR-020 screen 8): postRenewalAction, wrapping
// `POST /api/renewals/{id}/action` -- the insight card's own three actions (Start negotiation /
// Assign to me / Snooze, AC-3). `{id}` is the same `contractId` GET /api/renewals returns per row
// (see that operation's own doc comment above for why there is no separate "renewal id"). Fourth
// web epic to extend openapi/contigo-api.v1.json (see that file's own "repeating chore" provenance
// paragraph). Unlike every other write call in this file, a well-formed request against an
// unknown/cross-tenant contract id still succeeds -- RenewalsEndpointExtensions' own doc comment:
// Contigo.Renewals cannot reference Contigo.Documents.Contracts at all (ADR-002), so it structurally
// cannot 404 -- never special-cased here the way getContract360/getRenewalPriority special-case 404.
type PostRenewalActionResponses = paths["/api/renewals/{id}/action"]["post"]["responses"];
export type RenewalActionBody = PostRenewalActionResponses[200]["content"]["application/json"];
/** The closed three-state vocabulary (`RenewalActionStatus.ToString()`), read off the generated response type rather than hand-duplicated -- see `RenewalActionBody`'s own provenance above. */
export type RenewalActionStatusValue = RenewalActionBody["status"];

/**
 * `POST /api/renewals/{id}/action` request body. Hand-written, not generated -- see this file's
 * header comment for why (the generator does not parse `requestBody`). The backend's own
 * `RenewalActionRequest` types all three fields as nullable wire strings (validated, not trusted,
 * server-side) -- this client only ever sends real, already-known values (see
 * `src/routes/renewals/index.tsx`), so the stronger, non-optional shape here is honest about what
 * this app actually sends rather than mirroring the server's defensive nullability.
 */
export interface PostRenewalActionRequest {
  owner: string;
  status: RenewalActionStatusValue;
  action: string;
}

export interface PostRenewalActionResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The upserted row's current state, present only when `ok` is true. */
  action: RenewalActionBody | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E07/F04/US01/T01 (ask-contigo-ui, ADR-020 screen 7): askContigo, wrapping `POST
// /api/chat/query` (backend/src/Contigo.Api/ChatEndpointExtensions.cs) -- the routed answer/citations
// /abstain envelope behind `src/routes/ask/` (AC-1 route line, AC-2 citations, AC-3 abstain, AC-4
// states). Fourth web epic to extend openapi/contigo-api.v1.json beyond the R0/portfolio/contract-360
// /review set (see that file's own "repeating chore" provenance paragraph and its `askContigo`
// operation's own description for the full Structured-vs-Semantic / abstain-reason / citation-lookup
// gap notes). The request body is hand-written, like CreateWorkspaceRequest/CorrectContractRequest
// above -- the generator does not parse `requestBody` at all yet.
type AskContigoResponses = paths["/api/chat/query"]["post"]["responses"];
export type AskContigoResponseBody = AskContigoResponses[200]["content"]["application/json"];
export type AskContigoCitationBody = AskContigoResponseBody["citations"][number];
export type AskContigoIntent = AskContigoResponseBody["intent"];

/** `POST /api/chat/query` request body (ChatEndpointExtensions.ChatQueryRequest). Hand-written --
 * see this file's header comment for why. */
export interface AskContigoRequest {
  question: string;
}

export interface AskContigoResult {
  /**
   * True only on `200 OK` -- the backend operation's own OpenAPI description names this "always 200
   * once routing succeeds", so `false` here means a transport failure or a genuine `400` (blank
   * question, missing tenant header, or a retrieval/answer failure), never "the AI could not
   * determine an answer". That outcome is `ok: true` with `response.canDetermine === false` --
   * see `AskContigoResponseBody`'s own shape (screens.md #7 "abstain" / "unknown question fallback").
   */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The full routed answer/abstain envelope, present only when `ok` is true. */
  response: AskContigoResponseBody | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E08/F03/US01/T01 (quote-check-ui, ADR-018 route /quotes/:id, ADR-020 screen 10): uploadQuote,
// wrapping `POST /api/quotes` -- the Quote Check stepper's own "Extract" step entry point.
// Multipart, like uploadDocument above; supplier/currency/geography/purchaseDate are optional form
// fields (see web/openapi/contigo-api.v1.json's own description on this operation for why a quote
// uploaded without them still stores/extracts normally but cannot be benchmark-matched yet).
type UploadQuoteResponses = paths["/api/quotes"]["post"]["responses"];
export type UploadedQuote = UploadQuoteResponses[201]["content"]["application/json"];

export interface UploadQuoteFields {
  supplier?: string;
  currency?: string;
  geography?: string;
  /** `yyyy-MM-dd`, matching the backend's `DateOnly?` parameter. */
  purchaseDate?: string;
}

export interface UploadQuoteResult {
  /** True only on `201 Created`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The stored (and synchronously extracted/normalized) quote, present only when `ok` is true. */
  quote: UploadedQuote | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// getQuoteAssessment, wrapping `GET /api/quotes/{id}/assessment` -- the Extract/Assessment steps'
// shared read shape (see recalculateQuoteAssessment below for why src/routes/quotes/ actually calls
// the recalculate operation, not this one, for its own reads -- both return an identical
// `assessment` shape).
type GetQuoteAssessmentResponses = paths["/api/quotes/{id}/assessment"]["get"]["responses"];
export type QuoteAssessmentBody = GetQuoteAssessmentResponses[200]["content"]["application/json"];
export type QuoteLineAssessmentBody = QuoteAssessmentBody["lines"][number];

/**
 * `Contigo.Quotes.Domain.MarketPosition`'s real 3-value wire set, named here (not generated) for the
 * same reason `PortfolioRiskSeverity` above is: web/scripts/generate-api-client.mjs#renderSchemaType
 * drops a nullable union's `null` member whenever the schema also declares `enum`, so
 * `QuoteLineAssessmentBody["position"]` is generated as a bare `string | null`. See
 * web/openapi/contigo-api.v1.json's own `getQuoteAssessment` operation description for the full
 * explanation.
 */
export type QuoteMarketPosition = "BelowMarket" | "InLine" | "AboveMarket";

const QUOTE_MARKET_POSITIONS: readonly QuoteMarketPosition[] = ["BelowMarket", "InLine", "AboveMarket"];

export function isQuoteMarketPosition(value: string): value is QuoteMarketPosition {
  return (QUOTE_MARKET_POSITIONS as readonly string[]).includes(value);
}

export interface GetQuoteAssessmentResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** One entry per line on the quote, present only when `ok` is true. */
  assessment: QuoteAssessmentBody | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// recalculateQuoteAssessment, wrapping `POST /api/quotes/{id}/assessment/recalculate`. Called with
// an empty `mappings` array as this app's own "read the current assessment + unmatched lines" call
// (SkuMappingService.RecalculateAsync's own doc comment names this a valid, side-effect-free "pure
// refresh") -- there is no separate `GET` that returns `unmatchedLines`, and this operation's
// response is that shape's superset (adds `unmatchedLines`/`normalization` on top of the identical
// `assessment` GET /api/quotes/{id}/assessment itself returns), so src/routes/quotes/ never calls
// getQuoteAssessment directly; it is still wrapped above for API-contract completeness (the same
// "wrap the endpoint's full surface even if this app's own screen only ever calls it one way"
// convention getPortfolio's own doc comment already follows).
type RecalculateQuoteResponses = paths["/api/quotes/{id}/assessment/recalculate"]["post"]["responses"];
export type QuoteRecalculationBody = RecalculateQuoteResponses[200]["content"]["application/json"];
export type UnmatchedQuoteLineBody = QuoteRecalculationBody["unmatchedLines"][number];

/** One manual SKU-to-product mapping correction -- `Contigo.Quotes.Application.Normalization
 * .SkuMappingCorrection`'s wire shape. Hand-written, like `CorrectContractRequest` above: the
 * generator does not parse `requestBody` at all yet (see this file's own header comment). */
export interface SkuMappingCorrectionInput {
  sku: string;
  edition?: string | null;
  canonicalSku: string;
  canonicalEdition?: string | null;
  canonicalProductName?: string | null;
}

export interface RecalculateQuoteAssessmentResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** `mappingsAppliedCount`/`normalization`/`unmatchedLines`/`assessment`, present only when `ok` is true. */
  recalculation: QuoteRecalculationBody | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// captureNegotiationOutcome, wrapping `POST /api/negotiations/outcomes` -- the Negotiation step's
// own outcome-capture form. `NegotiationLeverTypeName` mirrors `Contigo.Quotes.Application.Strategy
// .NegotiationLeverType`'s 7-member closed vocabulary (see web/openapi/contigo-api.v1.json's own
// operation description for why this screen has no way to fetch AI-recommended levers/evidence --
// NegotiationStrategyService has no HTTP endpoint -- so this control lets a person pick from the
// same fixed set the capture endpoint itself validates against, rather than free text it would
// reject).
export type NegotiationLeverTypeName =
  | "Volume"
  | "Term"
  | "Utilization"
  | "Alternatives"
  | "QuarterEnd"
  | "Bundle"
  | "PaymentTerms";

export const NEGOTIATION_LEVER_TYPES: readonly NegotiationLeverTypeName[] = [
  "Volume",
  "Term",
  "Utilization",
  "Alternatives",
  "QuarterEnd",
  "Bundle",
  "PaymentTerms",
];

/** `POST /api/negotiations/outcomes` request body -- `NegotiationOutcomeCaptureRequest`'s wire
 * shape. `realizedSaving`/`discountPercent` are deliberately absent: `NegotiationOutcomeService
 * .CaptureAsync` always computes them itself (`NegotiationOutcomeCalculator.Compute`), never trusts
 * a caller-supplied figure for arithmetic it can derive exactly (Appendix C rule 6). */
export interface CaptureNegotiationOutcomeRequest {
  quoteId: string;
  originalQuoteTotal: number;
  targetPrice?: number | null;
  finalPrice: number;
  negotiationDurationDays: number;
  leversUsed: readonly NegotiationLeverTypeName[];
  savingsOpportunityId?: string | null;
}

type CaptureNegotiationOutcomeResponses = paths["/api/negotiations/outcomes"]["post"]["responses"];
export type NegotiationOutcomeBody = CaptureNegotiationOutcomeResponses[201]["content"]["application/json"];

export interface CaptureNegotiationOutcomeResult {
  /** True only on `201 Created`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** The recorded outcome (with server-computed `realizedSaving`/`discountPercent`), present only when `ok` is true. */
  outcome: NegotiationOutcomeBody | null;
  /** Plain-language failure reason (400/404 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// Task E08/F02/US01/T01 (savings-home, ADR-020 screen 9): getSavingsKpis, wrapping
// `GET /api/savings/kpis` -- the Home screen's 6 KPI cells (Annual spend analyzed, Savings
// identified, Savings realized, Savings in progress, Contracts analyzed, Upcoming renewals; product
// spec section 10.1). Fifth web epic to extend openapi/contigo-api.v1.json (see that file's own
// "repeating chore" provenance paragraph). This operation never reaches Contigo.Benchmark directly
// (it only aggregates Documents/Contracts and Savings data already at rest) -- see
// GetSavingsKpisResult's own doc comment for what a failed call means for this screen's "benchmark
// provider unreachable" state (screens.md #9).
type GetSavingsKpisResponses = paths["/api/savings/kpis"]["get"]["responses"];
export type SavingsKpiSummaryBody = GetSavingsKpisResponses[200]["content"]["application/json"];
export type SavingsSpendByCurrencyBody = SavingsKpiSummaryBody["annualSpendAnalyzed"][number];
export type SavingsRangeByCurrencyBody = SavingsKpiSummaryBody["savingsIdentified"][number];

export interface GetSavingsKpisResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /**
   * The six-row KPI summary, present only when `ok` is true. A `false` result here is this screen's
   * own honest trigger for screens.md #9's "error (benchmark provider unreachable; KPIs
   * stale-labelled)" state -- src/routes/home/ keeps whatever `kpis` value it last successfully
   * fetched on screen, tagged stale, rather than blanking the whole KPI row on a transient failure
   * (see src/routes/home/homeViewModel.ts#reduceKpiFetch).
   */
  kpis: SavingsKpiSummaryBody | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

// getSavingsOpportunities, wrapping `GET /api/savings` -- the Home screen's opportunities table
// (Opportunity/Type/Current spend/Estimated savings/Confidence/Owner/Status/Realized, screens.md
// #9). Never 404s -- an empty `items` array is a tenant's own honest "no opportunities yet" answer,
// the same convention getRenewals already follows for its own tenant-scoped list.
type GetSavingsOpportunitiesResponses = paths["/api/savings"]["get"]["responses"];
export type SavingsOpportunitiesPageBody = GetSavingsOpportunitiesResponses[200]["content"]["application/json"];
export type SavingsOpportunityBody = SavingsOpportunitiesPageBody["items"][number];

export interface GetSavingsOpportunitiesResult {
  /** True only on `200 OK`. */
  ok: boolean;
  /** HTTP status code, or `null` if the request never completed at all (e.g. DNS/network failure). */
  statusCode: number | null;
  /** Every opportunity for the caller's tenant, newest identified first (possibly empty), present only when `ok` is true. */
  opportunities: SavingsOpportunitiesPageBody | null;
  /** Plain-language failure reason (400 message, HTTP status text, or network-failure cause), present only when `ok` is false. */
  error: string | null;
}

export interface ApiClient {
  /**
   * Calls `GET /health` (operationId `getHealth` in
   * web/openapi/contigo-api.v1.json). Deliberately never throws on a
   * non-2xx response -- an "Unhealthy" 503 is a valid, expected answer from
   * a health probe, not a client error -- so callers (src/App.tsx) can
   * render `result.ok` directly without a try/catch. It resolves (rather
   * than throws) on a network failure too, for the same reason: `statusCode`
   * stays `null` and `body` carries the failure message.
   */
  getHealth(): Promise<HealthCheckResult>;
  /**
   * Calls `POST /api/workspaces` (operationId `createWorkspace`). Same
   * never-throws shape as getHealth(): a validation failure (400, e.g. a
   * blank name) is a normal, expected outcome the caller renders inline, not
   * an exception. See web/src/routes/signin/WorkspacePickerScreen.tsx for
   * the only caller today.
   */
  createWorkspace(request: CreateWorkspaceRequest): Promise<CreateWorkspaceResult>;
  /**
   * Calls `POST /api/workspaces/{tenantId}/invites` (operationId `inviteWorkspaceMember`). The
   * target tenant comes from the route, not an `X-Tenant-Id` header -- see that operation's own
   * description in openapi/contigo-api.v1.json. Same never-throws shape as `createWorkspace`: a
   * 400 (blank email, unrecognised role, duplicate membership) is a normal, expected outcome the
   * caller renders inline. See src/routes/workspace/members/ for the only caller today.
   */
  inviteWorkspaceMember(
    tenantId: string,
    request: InviteWorkspaceMemberRequest,
  ): Promise<InviteWorkspaceMemberResult>;
  /**
   * Calls `POST /api/documents` (operationId `uploadDocument`) as
   * `multipart/form-data` with a single `file` field -- the exact shape
   * `DocumentUploadEndpointTests.cs` (backend) enforces. `tenantId` is sent
   * as the interim `X-Tenant-Id` header every write endpoint requires today
   * (ADR-010 claim-based tenant resolution is not wired yet); see
   * src/routes/signin/workspaceStore.ts's own doc comment, which names this
   * exact call as the reason it keeps the current workspace id available.
   *
   * The backend runs the whole parse -> classify -> extract pipeline
   * *synchronously* before responding (task E02/F06/US01/T01's own
   * description in openapi/contigo-api.v1.json), so a resolved call already
   * carries a terminal (or near-terminal) `processingStatus` -- see
   * src/routes/documents/uploadPipeline.ts for how the UI turns that into
   * the 6-stage pipeline animation + result card. Same never-throws shape as
   * `createWorkspace`: a 400 (bad file/tenant) is a normal, expected outcome
   * the caller renders inline, not an exception.
   */
  uploadDocument(tenantId: string, file: File): Promise<UploadDocumentResult>;
  /**
   * Calls `GET /api/documents/{id}` (operationId `getDocument`) -- the
   * OpenAPI document's own description is "Read back one document's
   * metadata and processing status", which is this task's own name
   * (E06/F05/US02/T01, document-status-readback). Same never-throws shape
   * as the other calls: a `404` (no such document for this tenant) is a
   * normal, expected outcome the caller renders inline (see
   * src/routes/documents/documentTable.ts's "Classifying…" placeholder),
   * not an exception.
   */
  getDocument(tenantId: string, id: string): Promise<GetDocumentResult>;
  /**
   * Calls `GET /api/contracts` (operationId `getPortfolio`) -- the portfolio list behind
   * `src/routes/contracts/` (AC-1 filters, AC-2 attention strip, AC-3 sort/tint, AC-4 states). Same
   * never-throws shape as every other call here: a `400` (malformed filter/page query parameter) is a
   * normal, expected outcome the caller renders inline, not an exception. `query` is optional and, when
   * omitted, fetches the tenant's whole first page unfiltered -- `src/routes/contracts/index.tsx` always
   * calls it that way and does every filter/attention-bucket computation client-side (see that file's own
   * header comment for why), but the full query surface is still wired here so this wrapper stays an
   * honest, complete mirror of the real endpoint rather than a screen-shaped subset of it.
   */
  getPortfolio(tenantId: string, query?: PortfolioQueryParams): Promise<GetPortfolioResult>;
  /**
   * Calls `GET /api/contracts/{id}` (operationId `getContract360`) -- the Contract 360 header +
   * 10-tab aggregate behind `src/routes/contracts/contract360/` (ADR-020 screen 5). Same
   * never-throws shape as every other call here: a `404` (no such contract for this tenant) is a
   * normal, expected outcome the caller renders as a named "not found" state, not an exception.
   */
  getContract360(tenantId: string, id: string): Promise<GetContract360Result>;
  /**
   * Calls `GET /api/renewals` (operationId `getRenewals`) -- the tenant's whole auto-renewing
   * pipeline. Contract 360's Overview tab finds this contract's own entry by `contractId` to source
   * the real recommendedAction/explanation text (see `GetRenewalsResult`'s own doc comment for why).
   * Never throws; a `400` renders inline.
   */
  getRenewals(tenantId: string): Promise<GetRenewalsResult>;
  /**
   * Calls `GET /api/renewals/{contractId}/priority` (operationId `getRenewalPriority`) -- the
   * explainable priority-score breakdown for one contract, regardless of auto-renewal status. Same
   * never-throws shape as every other call here: a `404` is a normal, expected outcome.
   */
  getRenewalPriority(tenantId: string, contractId: string): Promise<GetRenewalPriorityResult>;
  /**
   * Calls `GET /api/contracts/{id}/corrections` (operationId `getCorrectionHistory`) -- the real,
   * newest-first correction trail behind `src/routes/contracts/review/` (ADR-020 screen 6). Same
   * never-throws shape as every other call here: a `404` is a normal, expected outcome (no such
   * contract for this tenant); a contract that exists but has never been corrected is `ok: true`
   * with an empty `history` array, not a 404.
   */
  getCorrectionHistory(tenantId: string, id: string): Promise<GetCorrectionHistoryResult>;
  /**
   * Calls `PATCH /api/contracts/{id}` (operationId `correctContract`) -- the review screen's real
   * "Correct" write path. Same never-throws shape as every other call here: a `400` (unknown field,
   * unparsable value, or a no-op correction) is a normal, expected outcome the caller renders
   * inline, not an exception.
   */
  correctContract(tenantId: string, id: string, request: CorrectContractRequest): Promise<CorrectContractResult>;
  /**
   * Calls `POST /api/renewals/{id}/action` (operationId `postRenewalAction`) -- upserts the one
   * `RenewalAction` row for (tenant, contractId). Same never-throws shape as every other call here:
   * a `400` (blank owner/action, or an unrecognized status) is a normal, expected outcome the
   * caller renders inline. Unlike `getContract360`/`getRenewalPriority`/`correctContract`, this
   * never 404s even for an unknown contract id -- see `RenewalActionBody`'s own doc comment.
   */
  postRenewalAction(
    tenantId: string,
    contractId: string,
    request: PostRenewalActionRequest,
  ): Promise<PostRenewalActionResult>;
  /**
   * Calls `POST /api/quotes` (operationId `uploadQuote`) as `multipart/form-data` -- the Quote
   * Check stepper's own upload entry point (there is no separate "new quote" screen; ADR-018 names
   * only the detail route `/quotes/:id`, so `src/routes/quotes/index.tsx` renders this call's own
   * upload form when no quote id is in the route yet). Same never-throws shape as `uploadDocument`.
   */
  uploadQuote(tenantId: string, file: File, fields?: UploadQuoteFields): Promise<UploadQuoteResult>;
  /**
   * Calls `GET /api/quotes/{id}/assessment` (operationId `getQuoteAssessment`). Same never-throws
   * shape as every other call here; a `404` is a normal, expected outcome. See
   * `recalculateQuoteAssessment` below for why `src/routes/quotes/` calls that operation, not this
   * one, for its own reads.
   */
  getQuoteAssessment(tenantId: string, id: string): Promise<GetQuoteAssessmentResult>;
  /**
   * Calls `POST /api/quotes/{id}/assessment/recalculate` (operationId `recalculateQuoteAssessment`)
   * -- `mappings` defaults to an empty array, the endpoint's own documented "pure refresh" read (see
   * `RecalculateQuoteAssessmentResult`'s own doc comment). Same never-throws shape as every other
   * call here; a `404` is a normal, expected outcome.
   */
  recalculateQuoteAssessment(
    tenantId: string,
    id: string,
    mappings?: readonly SkuMappingCorrectionInput[],
  ): Promise<RecalculateQuoteAssessmentResult>;
  /**
   * Calls `POST /api/negotiations/outcomes` (operationId `captureNegotiationOutcome`) -- the
   * Negotiation step's outcome-capture form. Same never-throws shape as every other call here: a
   * `400` (e.g. an invalid `leversUsed` entry) or `404` (unknown quote) is a normal, expected
   * outcome the caller renders inline.
   */
  captureNegotiationOutcome(
    tenantId: string,
    request: CaptureNegotiationOutcomeRequest,
  ): Promise<CaptureNegotiationOutcomeResult>;

  askContigo(tenantId: string, request: AskContigoRequest): Promise<AskContigoResult>;
  /**
   * Calls `GET /api/savings/kpis` (operationId `getSavingsKpis`) -- the Home screen's 6 KPI cells.
   * Same never-throws shape as every other call here: a `400` (missing/invalid tenant header) is a
   * normal, expected outcome the caller renders inline, not an exception.
   */
  getSavingsKpis(tenantId: string): Promise<GetSavingsKpisResult>;
  /**
   * Calls `GET /api/savings` (operationId `getSavingsOpportunities`) -- the Home screen's
   * opportunities table. Same never-throws shape as every other call here; never 404s (an empty
   * `items` array is a normal, expected "nothing yet" answer).
   */
  getSavingsOpportunities(tenantId: string): Promise<GetSavingsOpportunitiesResult>;
}


/**
 * Builds the API client from runtime config (ADR-012 "config, not code";
 * `AppConfig.apiBaseUrl`, see src/config/appConfig.ts). `baseUrl` is expected
 * to be an absolute origin with no path (e.g.
 * "https://api.dev.contigo.example"); every operation resolves its path
 * against it with the platform `URL` parser rather than hand-rolled string
 * concatenation.
 */
export function createApiClient(baseUrl: string): ApiClient {
  return {
    async getHealth() {
      let response: Response;
      try {
        response = await fetch(new URL("/health", baseUrl), { cache: "no-store" });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          body: `Unable to reach ${baseUrl}/health. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      const body = await response.text();
      return { ok: response.ok, statusCode: response.status, body };
    },

    async createWorkspace(request) {
      let response: Response;
      try {
        response = await fetch(new URL("/api/workspaces", baseUrl), {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(request),
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          workspace: null,
          error: `Unable to reach ${baseUrl}/api/workspaces. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 201) {
        const workspace = (await response.json()) as CreateWorkspaceBody;
        return { ok: true, statusCode: 201, workspace, error: null };
      }

      // Results.BadRequest(string) (WorkspaceEndpointExtensions.cs) serializes
      // the message as a bare JSON string, not text/plain -- parse as JSON
      // first and only fall back to statusText if that fails (e.g. a 5xx from
      // a proxy/gateway in front of the API, which never ran this handler).
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, workspace: null, error };
    },

    async inviteWorkspaceMember(tenantId, request) {
      let response: Response;
      try {
        response = await fetch(
          new URL(`/api/workspaces/${encodeURIComponent(tenantId)}/invites`, baseUrl),
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(request),
            cache: "no-store",
          },
        );
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          member: null,
          error: `Unable to reach ${baseUrl}/api/workspaces/${tenantId}/invites. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 201) {
        const member = (await response.json()) as InvitedMemberBody;
        return { ok: true, statusCode: 201, member, error: null };
      }

      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, member: null, error };
    },

    async uploadDocument(tenantId, file) {
      const formData = new FormData();
      // `file` is already a `File` (extends `Blob` with its own `.name`), so
      // FormData uses that name automatically -- no third `filename` arg
      // needed (see MDN FormData.append()).
      formData.append("file", file);

      let response: Response;
      try {
        response = await fetch(new URL("/api/documents", baseUrl), {
          method: "POST",
          headers: { "X-Tenant-Id": tenantId },
          body: formData,
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          document: null,
          error: `Unable to reach ${baseUrl}/api/documents. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 201) {
        const document = (await response.json()) as UploadedDocument;
        return { ok: true, statusCode: 201, document, error: null };
      }

      // Same Results.BadRequest(string) shape as createWorkspace's 400 above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, document: null, error };
    },

    async getDocument(tenantId, id) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/documents/${encodeURIComponent(id)}`, baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          document: null,
          error: `Unable to reach ${baseUrl}/api/documents/${id}. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const document = (await response.json()) as ReadBackDocument;
        return { ok: true, statusCode: 200, document, error: null };
      }

      // The OpenAPI document's own 404 response carries no body at all
      // (unlike the 400s' `Results.BadRequest(string)` JSON-string bodies),
      // matching Results.NotFound()'s empty response -- so this status is
      // special-cased rather than attempting response.json() against an
      // empty body.
      if (response.status === 404) {
        return { ok: false, statusCode: 404, document: null, error: `No document found for id ${id}.` };
      }

      // Same Results.BadRequest(string) shape as uploadDocument's 400 above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, document: null, error };
    },

    async getPortfolio(tenantId, query = {}) {
      const url = new URL("/api/contracts", baseUrl);
      // Only ever sets a key when the caller actually supplied it -- an absent query parameter must
      // reach the backend as "not present at all", not as an empty string, to match
      // PortfolioEndpointExtensions' own "absent = not filtered" parsing.
      if (query.supplierId !== undefined) url.searchParams.set("supplierId", query.supplierId);
      if (query.status !== undefined) url.searchParams.set("status", query.status);
      if (query.risk !== undefined) url.searchParams.set("risk", query.risk);
      if (query.autoRenewal !== undefined) url.searchParams.set("autoRenewal", String(query.autoRenewal));
      if (query.minAnnualSpend !== undefined) url.searchParams.set("minAnnualSpend", String(query.minAnnualSpend));
      if (query.maxAnnualSpend !== undefined) url.searchParams.set("maxAnnualSpend", String(query.maxAnnualSpend));
      if (query.renewalFrom !== undefined) url.searchParams.set("renewalFrom", query.renewalFrom);
      if (query.renewalTo !== undefined) url.searchParams.set("renewalTo", query.renewalTo);
      if (query.page !== undefined) url.searchParams.set("page", String(query.page));
      if (query.pageSize !== undefined) url.searchParams.set("pageSize", String(query.pageSize));

      let response: Response;
      try {
        response = await fetch(url, { headers: { "X-Tenant-Id": tenantId }, cache: "no-store" });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          portfolio: null,
          error: `Unable to reach ${baseUrl}/api/contracts. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const portfolio = (await response.json()) as PortfolioPageBody;
        return { ok: true, statusCode: 200, portfolio, error: null };
      }

      // Same Results.BadRequest(string) shape as the other calls' 400s above (also covers a 503 from a
      // proxy/gateway in front of the API, which never ran this handler at all -- AC-4's "error 503 +
      // retry" state renders whatever plain-language error lands here rather than assuming this shape).
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, portfolio: null, error };
    },

    async getContract360(tenantId, id) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/contracts/${encodeURIComponent(id)}`, baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          contract: null,
          error: `Unable to reach ${baseUrl}/api/contracts/${id}. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const contract = (await response.json()) as Contract360Body;
        return { ok: true, statusCode: 200, contract, error: null };
      }

      // Same empty-body 404 shape as getDocument's own 404 above (Results.NotFound()).
      if (response.status === 404) {
        return { ok: false, statusCode: 404, contract: null, error: `No contract found for id ${id}.` };
      }

      // Same Results.BadRequest(string) shape as the other calls' 400s above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, contract: null, error };
    },

    async getRenewals(tenantId) {
      let response: Response;
      try {
        response = await fetch(new URL("/api/renewals", baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          renewals: null,
          error: `Unable to reach ${baseUrl}/api/renewals. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const renewals = (await response.json()) as RenewalsPageBody;
        return { ok: true, statusCode: 200, renewals, error: null };
      }

      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, renewals: null, error };
    },

    async getRenewalPriority(tenantId, contractId) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/renewals/${encodeURIComponent(contractId)}/priority`, baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          priority: null,
          error: `Unable to reach ${baseUrl}/api/renewals/${contractId}/priority. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const priority = (await response.json()) as RenewalPriorityBody;
        return { ok: true, statusCode: 200, priority, error: null };
      }

      if (response.status === 404) {
        return { ok: false, statusCode: 404, priority: null, error: `No contract found for id ${contractId}.` };
      }

      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, priority: null, error };
    },

    async getCorrectionHistory(tenantId, id) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/contracts/${encodeURIComponent(id)}/corrections`, baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          history: null,
          error: `Unable to reach ${baseUrl}/api/contracts/${id}/corrections. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const history = (await response.json()) as CorrectionHistoryPageBody;
        return { ok: true, statusCode: 200, history, error: null };
      }

      // Same empty-body 404 shape as getContract360's own 404 above (Results.NotFound()).
      if (response.status === 404) {
        return { ok: false, statusCode: 404, history: null, error: `No contract found for id ${id}.` };
      }

      // Same Results.BadRequest(string) shape as the other calls' 400s above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, history: null, error };
    },

    async correctContract(tenantId, id, request) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/contracts/${encodeURIComponent(id)}`, baseUrl), {
          method: "PATCH",
          headers: { "Content-Type": "application/json", "X-Tenant-Id": tenantId },
          body: JSON.stringify(request),
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          correction: null,
          error: `Unable to reach ${baseUrl}/api/contracts/${id}. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const correction = (await response.json()) as ContractCorrectionBody;
        return { ok: true, statusCode: 200, correction, error: null };
      }

      // Same empty-body 404 shape as getContract360's own 404 above (Results.NotFound()).
      if (response.status === 404) {
        return { ok: false, statusCode: 404, correction: null, error: `No contract found for id ${id}.` };
      }

      // Same Results.BadRequest(string) shape as the other calls' 400s above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, correction: null, error };
    },

    async askContigo(tenantId, request) {
      let response: Response;
      try {
        response = await fetch(new URL("/api/chat/query", baseUrl), {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-Tenant-Id": tenantId },
          body: JSON.stringify(request),
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          response: null,
          error: `Unable to reach ${baseUrl}/api/chat/query. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const body = (await response.json()) as AskContigoResponseBody;
        return { ok: true, statusCode: 200, response: body, error: null };
      }

      // Same Results.BadRequest(string) shape as the other calls' 400s above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, response: null, error };
    },

    async postRenewalAction(tenantId, contractId, request) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/renewals/${encodeURIComponent(contractId)}/action`, baseUrl), {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-Tenant-Id": tenantId },
          body: JSON.stringify(request),
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          action: null,
          error: `Unable to reach ${baseUrl}/api/renewals/${contractId}/action. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const action = (await response.json()) as RenewalActionBody;
        return { ok: true, statusCode: 200, action, error: null };
      }

      // Same Results.BadRequest(string) shape as every other call's 400 above. Never a 404 -- see
      // this method's own doc comment / RenewalActionBody's header comment for why.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, action: null, error };
    },

    async uploadQuote(tenantId, file, fields = {}) {
      const formData = new FormData();
      formData.append("file", file);
      if (fields.supplier !== undefined) formData.append("supplier", fields.supplier);
      if (fields.currency !== undefined) formData.append("currency", fields.currency);
      if (fields.geography !== undefined) formData.append("geography", fields.geography);
      if (fields.purchaseDate !== undefined) formData.append("purchaseDate", fields.purchaseDate);

      let response: Response;
      try {
        response = await fetch(new URL("/api/quotes", baseUrl), {
          method: "POST",
          headers: { "X-Tenant-Id": tenantId },
          body: formData,
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          quote: null,
          error: `Unable to reach ${baseUrl}/api/quotes. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 201) {
        const quote = (await response.json()) as UploadedQuote;
        return { ok: true, statusCode: 201, quote, error: null };
      }

      // Same Results.BadRequest(string) shape as uploadDocument's 400 above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, quote: null, error };
    },

    async getQuoteAssessment(tenantId, id) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/quotes/${encodeURIComponent(id)}/assessment`, baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          assessment: null,
          error: `Unable to reach ${baseUrl}/api/quotes/${id}/assessment. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const assessment = (await response.json()) as QuoteAssessmentBody;
        return { ok: true, statusCode: 200, assessment, error: null };
      }

      // Unlike getContract360/getDocument above, this operation's own 404 DOES carry a body
      // (QuotesEndpointExtensions.GetAssessmentAsync calls Results.NotFound(result.Error), not the
      // bare, empty-body Results.NotFound() those other endpoints use) -- read it the same way a
      // 400 is read below, rather than fabricating a generic message.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, assessment: null, error };
    },

    async recalculateQuoteAssessment(tenantId, id, mappings = []) {
      let response: Response;
      try {
        response = await fetch(new URL(`/api/quotes/${encodeURIComponent(id)}/assessment/recalculate`, baseUrl), {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-Tenant-Id": tenantId },
          body: JSON.stringify({ mappings }),
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          recalculation: null,
          error: `Unable to reach ${baseUrl}/api/quotes/${id}/assessment/recalculate. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const recalculation = (await response.json()) as QuoteRecalculationBody;
        return { ok: true, statusCode: 200, recalculation, error: null };
      }

      // Same real-body 404 as getQuoteAssessment above (SkuMappingService.QuoteNotFoundError via
      // Results.NotFound(result.Error)), plus the same Results.BadRequest(string) 400 shape as
      // every other call here.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, recalculation: null, error };
    },

    async captureNegotiationOutcome(tenantId, request) {
      let response: Response;
      try {
        response = await fetch(new URL("/api/negotiations/outcomes", baseUrl), {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-Tenant-Id": tenantId },
          body: JSON.stringify(request),
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          outcome: null,
          error: `Unable to reach ${baseUrl}/api/negotiations/outcomes. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 201) {
        const outcome = (await response.json()) as NegotiationOutcomeBody;
        return { ok: true, statusCode: 201, outcome, error: null };
      }

      // Same real-body 404 as getQuoteAssessment above (NegotiationOutcomeService
      // .QuoteNotFoundError via Results.NotFound(result.Error)), plus the same
      // Results.BadRequest(string) 400 shape as every other call here.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, outcome: null, error };
    },

    async getSavingsKpis(tenantId) {
      let response: Response;
      try {
        response = await fetch(new URL("/api/savings/kpis", baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          kpis: null,
          error: `Unable to reach ${baseUrl}/api/savings/kpis. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const kpis = (await response.json()) as SavingsKpiSummaryBody;
        return { ok: true, statusCode: 200, kpis, error: null };
      }

      // Same Results.BadRequest(string) shape as the other calls' 400s above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, kpis: null, error };
    },

    async getSavingsOpportunities(tenantId) {
      let response: Response;
      try {
        response = await fetch(new URL("/api/savings", baseUrl), {
          headers: { "X-Tenant-Id": tenantId },
          cache: "no-store",
        });
      } catch (cause) {
        return {
          ok: false,
          statusCode: null,
          opportunities: null,
          error: `Unable to reach ${baseUrl}/api/savings. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
        };
      }

      if (response.status === 200) {
        const opportunities = (await response.json()) as SavingsOpportunitiesPageBody;
        return { ok: true, statusCode: 200, opportunities, error: null };
      }

      // Same Results.BadRequest(string) shape as the other calls' 400s above.
      let error: string;
      try {
        const errorBody: unknown = await response.json();
        error = typeof errorBody === "string" ? errorBody : JSON.stringify(errorBody);
      } catch {
        error = `Request failed with HTTP ${response.status} ${response.statusText}.`;
      }

      return { ok: false, statusCode: response.status, opportunities: null, error };
    },
  };
}
