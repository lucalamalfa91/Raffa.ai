// OIDC Authorization Code + PKCE via MSAL (ADR-010, ADR-012 "mature OIDC/PKCE
// library support (e.g. MSAL)"; api-consumption.md "Refresh/session handling
// uses the provider's standard flow (MSAL)").
//
// PublicClientApplication is, by construction, a *public* client: this
// Configuration shape has no field for a client secret anywhere (see
// @azure/msal-browser's BrowserAuthOptions type) -- the PKCE flow is the only
// way this SDK obtains tokens, which is what AC-1 ("no client secret in
// bundle") requires structurally, not just by convention.
import { BrowserCacheLocation, type Configuration, type RedirectRequest } from "@azure/msal-browser";
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
