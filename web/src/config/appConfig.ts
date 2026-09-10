// Runtime config injection (ADR-012, client-architect api-consumption.md).
//
// ADR-012 requires the API base URL and OIDC authority to come from
// "per-environment config (runtime injection), not hard-coded, so the same
// bundle deploys to dev and demo". This matters mechanically, not just
// stylistically: web.yml's `build` job runs `npm run build` exactly once and
// uploads a single `web-dist` artifact; the `deploy` job downloads that same
// artifact for either `dev` (push to main) or `demo` (workflow_call reuse,
// ADR-016). One compiled bundle is deployed unchanged to both environments,
// so per-env values cannot be baked in at build time (e.g. Vite `VITE_*`
// statics) -- they must be resolved from the deployed origin at request time.
//
// This module fetches that config from a same-origin static asset,
// `/config.json`, at app boot (see main.tsx). `public/config.json` ships a
// safe, non-secret localhost placeholder for local dev; per-environment
// values are the deploy pipeline's responsibility to substitute into the
// downloaded `web-dist/config.json` before the Static Web Apps deploy step
// (see web.yml "Write per-environment config.json" and web/README.md
// "Runtime config injection").
export interface AppConfig {
  /** Origin the SPA calls for every API request, e.g. "https://api.dev.raffa.example". */
  apiBaseUrl: string;
  /** OIDC authority (issuer) this environment's Entra tenant/app registration trusts (ADR-010). */
  oidcAuthority: string;
  /** Public-client (no secret) Entra app registration id for this environment (ADR-010). */
  oidcClientId: string;
  /** Redirect URI registered on the public client for this environment's web origin. */
  oidcRedirectUri: string;
  /**
   * API scopes requested at login (e.g. "api://<api-client-id>/Raffa.Read").
   * Placeholder names until the API surface fixes them (ADR-010 "Assumptions");
   * kept config-driven here so this task does not invent the final value.
   */
  oidcApiScopes: string[];
}

const REQUIRED_STRING_FIELDS = [
  "apiBaseUrl",
  "oidcAuthority",
  "oidcClientId",
  "oidcRedirectUri",
] as const;

/** Thrown for any malformed or unreachable runtime config -- always fail loudly, never fall back to a guessed value. */
export class AppConfigError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AppConfigError";
  }
}

/**
 * Validates an already-parsed JSON payload against the `AppConfig` shape.
 * Exported separately from `loadAppConfig` so tests can exercise validation
 * without mocking `fetch`.
 */
export function validateAppConfig(raw: unknown, sourceLabel = "config"): AppConfig {
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    throw new AppConfigError(
      `Runtime config (${sourceLabel}) must be a JSON object, got ${Array.isArray(raw) ? "an array" : typeof raw}.`,
    );
  }

  const candidate = raw as Record<string, unknown>;

  for (const field of REQUIRED_STRING_FIELDS) {
    const value = candidate[field];
    if (typeof value !== "string" || value.trim() === "") {
      throw new AppConfigError(
        `Runtime config (${sourceLabel}) is missing required non-empty string field "${field}".`,
      );
    }
  }

  const scopes = candidate.oidcApiScopes;
  if (
    !Array.isArray(scopes) ||
    scopes.length === 0 ||
    !scopes.every((scope) => typeof scope === "string" && scope.trim() !== "")
  ) {
    throw new AppConfigError(
      `Runtime config (${sourceLabel}) field "oidcApiScopes" must be a non-empty array of non-empty strings.`,
    );
  }

  return {
    apiBaseUrl: candidate.apiBaseUrl as string,
    oidcAuthority: candidate.oidcAuthority as string,
    oidcClientId: candidate.oidcClientId as string,
    oidcRedirectUri: candidate.oidcRedirectUri as string,
    oidcApiScopes: scopes as string[],
  };
}

/**
 * Fetches and validates the runtime config the deployed origin serves
 * alongside the static bundle. Never caches across calls, and never falls
 * back to a hard-coded default on failure (api-consumption.md: "a dev/demo
 * mismatch fails loudly, not silently").
 */
export async function loadAppConfig(configUrl = "/config.json"): Promise<AppConfig> {
  let response: Response;
  try {
    response = await fetch(configUrl, { cache: "no-store" });
  } catch (cause) {
    throw new AppConfigError(
      `Unable to reach runtime config at "${configUrl}". The web client cannot start without it. ` +
        `Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
    );
  }

  if (!response.ok) {
    throw new AppConfigError(
      `Runtime config request to "${configUrl}" failed with HTTP ${response.status} ${response.statusText}.`,
    );
  }

  let raw: unknown;
  try {
    raw = await response.json();
  } catch (cause) {
    throw new AppConfigError(
      `Runtime config at "${configUrl}" is not valid JSON. Cause: ${cause instanceof Error ? cause.message : String(cause)}`,
    );
  }

  return validateAppConfig(raw, configUrl);
}
