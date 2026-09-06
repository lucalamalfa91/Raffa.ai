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
  };
}
