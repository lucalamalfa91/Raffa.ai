// OIDC Authorization Code + PKCE via MSAL (ADR-010, ADR-012 "mature OIDC/PKCE
// library support (e.g. MSAL)"; api-consumption.md "Refresh/session handling
// uses the provider's standard flow (MSAL)").
//
// PublicClientApplication is, by construction, a *public* client: this
// Configuration shape has no field for a client secret anywhere (see
// @azure/msal-browser's BrowserAuthOptions type) -- the PKCE flow is the only
// way this SDK obtains tokens, which is what AC-1 ("no client secret in
// bundle") requires structurally, not just by convention.
import {
  BrowserCacheLocation,
  type Configuration,
  type HandleRedirectPromiseOptions,
  type RedirectRequest,
} from "@azure/msal-browser";
import type { AppConfig } from "../config/appConfig";

/**
 * Builds the MSAL `Configuration` from runtime, per-environment config
 * (ADR-012 "config, not code"). Every value that differs between `dev` and
 * `demo` (client id, authority, redirect URI) comes from `AppConfig`; nothing
 * here is environment-specific or hard-coded.
 */
export function buildMsalConfig(appConfig: AppConfig): Configuration {
  return {
    auth: {
      clientId: appConfig.oidcClientId,
      authority: appConfig.oidcAuthority,
      redirectUri: appConfig.oidcRedirectUri,
      postLogoutRedirectUri: appConfig.oidcRedirectUri,
    },
    cache: {
      // sessionStorage over localStorage: tokens do not outlive the tab, and
      // are not shared across tabs -- a narrower blast radius for XSS than
      // the MSAL default of localStorage.
      cacheLocation: BrowserCacheLocation.SessionStorage,
    },
  };
}

/**
 * Task E14/F03/US02/T01 (wave w14; ADR-025 Rule C10a, its own w14 footer): explicit, not left to
 * the library default. Rule C10a was originally written as an `auth`-level `Configuration` field
 * (`navigateToLoginRequestUrl: false`) -- that field does not exist on the installed
 * `@azure/msal-browser@^5.21.0`'s `BrowserAuthOptions` (confirmed against
 * `node_modules/@azure/msal-browser/types/config/Configuration.d.ts`, which lists exactly `auth` /
 * `cache` / `system` / `experimental` / `telemetry`, none of them carrying this flag). In this
 * major version the flag moved to a **per-call** `handleRedirectPromise(options)` argument
 * (`HandleRedirectPromiseOptions`, `types/request/HandleRedirectPromiseOptions.d.ts`) -- there is
 * no config-level equivalent any more.
 *
 * `main.tsx` is this app's one caller of `handleRedirectPromise` (`@azure/msal-react`'s
 * `MsalProvider` would otherwise be -- it calls `instance.handleRedirectPromise()` bare, with no
 * options, and exposes no prop to inject any: `node_modules/@azure/msal-react/dist/MsalProvider.js`).
 * `main.tsx` calls `handleRedirectPromise(handleRedirectPromiseOptions)` itself, once, before
 * `<MsalProvider>` ever mounts; `MsalProvider`'s own later bare call reuses MSAL's internally
 * cached redirect-promise result (the exact mechanism its own source comments on: "If
 * handleRedirectPromise returns a cached promise the necessary events may not be fired") rather
 * than re-running with the library default -- the same call-it-yourself-first pattern MSAL's own
 * docs give for supplying custom `handleRedirectPromise` options under `msal-react`.
 *
 * Why this still matters with Rule C10b (`routes/invite/accept/index.tsx`'s synchronous,
 * mount-time address-bar clear) already in place: C10b strips `/invite/accept#<token>` from the
 * visible URL before this app's first paint, but MSAL's own default (`navigateToLoginRequestUrl:
 * true`) still *caches* whatever the current href was at the moment a redirect starts as the
 * "login request URL" to restore later -- a structural guarantee independent of this component's
 * own render-ordering, which is exactly why the ADR asks for both: "C10a survives a refactor of
 * *this* ordering, C10b survives a change to MSAL's own default."
 */
export const handleRedirectPromiseOptions: HandleRedirectPromiseOptions = {
  navigateToLoginRequestUrl: false,
};

/**
 * The scopes requested at login. ADR-010 names `Raffa.Read`/`Raffa.Write`
 * as placeholder API scopes pending the API surface being fixed; this stays
 * config-driven (`AppConfig.oidcApiScopes`) rather than hard-coding an
 * App ID URI this task cannot confirm.
 */
export function buildLoginRequest(appConfig: AppConfig): RedirectRequest {
  return {
    scopes: appConfig.oidcApiScopes,
  };
}
