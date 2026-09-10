import { describe, expect, it } from "vitest";
import { BrowserCacheLocation } from "@azure/msal-browser";
import { buildLoginRequest, buildMsalConfig } from "../../src/auth/msalConfig";
import type { AppConfig } from "../../src/config/appConfig";

const appConfig: AppConfig = {
  apiBaseUrl: "https://api.dev.raffa.example",
  oidcAuthority: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000",
  oidcClientId: "11111111-1111-1111-1111-111111111111",
  oidcRedirectUri: "https://web.dev.raffa.example",
  oidcApiScopes: [
    "api://11111111-1111-1111-1111-111111111111/Raffa.Read",
    "api://11111111-1111-1111-1111-111111111111/Raffa.Write",
  ],
};

describe("buildMsalConfig", () => {
  it("wires client id, authority and redirect URIs from runtime config (ADR-012 config-not-code)", () => {
    const config = buildMsalConfig(appConfig);
    expect(config.auth.clientId).toBe(appConfig.oidcClientId);
    expect(config.auth.authority).toBe(appConfig.oidcAuthority);
    expect(config.auth.redirectUri).toBe(appConfig.oidcRedirectUri);
    expect(config.auth.postLogoutRedirectUri).toBe(appConfig.oidcRedirectUri);
  });

  it("never carries a client secret (public client / PKCE only, AC-1)", () => {
    const config = buildMsalConfig(appConfig);
    expect(config.auth).not.toHaveProperty("clientSecret");
    // Belt-and-braces: nothing in the whole built config should ever contain
    // the substring "secret" -- MSAL's public-client Configuration type has
    // no such field, but this guards against a future field being added and
    // accidentally populated.
    expect(JSON.stringify(config).toLowerCase()).not.toContain("secret");
  });

  it("persists the MSAL cache in sessionStorage, not localStorage", () => {
    const config = buildMsalConfig(appConfig);
    expect(config.cache?.cacheLocation).toBe(BrowserCacheLocation.SessionStorage);
  });
});

describe("buildLoginRequest", () => {
  it("requests exactly the API scopes named in runtime config", () => {
    expect(buildLoginRequest(appConfig).scopes).toEqual(appConfig.oidcApiScopes);
  });
});
