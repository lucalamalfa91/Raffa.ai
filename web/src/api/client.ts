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
  };
}
