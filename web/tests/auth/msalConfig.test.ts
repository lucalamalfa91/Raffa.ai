import { describe, expect, it, vi } from "vitest";
import { BrowserCacheLocation, InteractionRequiredAuthError } from "@azure/msal-browser";
import type { AccountInfo, IPublicClientApplication } from "@azure/msal-browser";
import { acquireApiAccessToken, buildLoginRequest, buildMsalConfig } from "../../src/auth/msalConfig";
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

describe("acquireApiAccessToken (task E18/F01/US02/T01, NW-05; ADR-012 w15 footer clause 1)", () => {
  const account = { username: "buyer@acme.example", homeAccountId: "home-1" } as unknown as AccountInfo;

  it("resolves null without acquiring anything when no account is signed in", async () => {
    const acquireTokenSilent = vi.fn();
    const acquireTokenPopup = vi.fn();
    const instance = {
      getActiveAccount: vi.fn().mockReturnValue(null),
      getAllAccounts: vi.fn().mockReturnValue([]),
      acquireTokenSilent,
      acquireTokenPopup,
      acquireTokenRedirect: vi.fn(),
    } as unknown as IPublicClientApplication;

    const token = await acquireApiAccessToken(instance, appConfig);

    expect(token).toBeNull();
    expect(acquireTokenSilent).not.toHaveBeenCalled();
    expect(acquireTokenPopup).not.toHaveBeenCalled();
  });

  it("falls back to the first cached account when none is active (mirrors main.tsx's own former fallback)", async () => {
    const acquireTokenSilent = vi.fn().mockResolvedValue({ accessToken: "silent-token" });
    const instance = {
      getActiveAccount: vi.fn().mockReturnValue(null),
      getAllAccounts: vi.fn().mockReturnValue([account]),
      acquireTokenSilent,
      acquireTokenPopup: vi.fn(),
      acquireTokenRedirect: vi.fn(),
    } as unknown as IPublicClientApplication;

    const token = await acquireApiAccessToken(instance, appConfig);

    expect(token).toBe("silent-token");
    expect(acquireTokenSilent).toHaveBeenCalledWith(
      expect.objectContaining({ scopes: appConfig.oidcApiScopes, account }),
    );
  });

  it("returns the acquireTokenSilent access token via {scopes, account}, and never calls the popup or redirect fallback", async () => {
    const acquireTokenSilent = vi.fn().mockResolvedValue({ accessToken: "silent-token" });
    const acquireTokenPopup = vi.fn();
    const acquireTokenRedirect = vi.fn();
    const instance = {
      getActiveAccount: vi.fn().mockReturnValue(account),
      getAllAccounts: vi.fn().mockReturnValue([account]),
      acquireTokenSilent,
      acquireTokenPopup,
      acquireTokenRedirect,
    } as unknown as IPublicClientApplication;

    const token = await acquireApiAccessToken(instance, appConfig);

    expect(token).toBe("silent-token");
    expect(acquireTokenSilent).toHaveBeenCalledWith(
      expect.objectContaining({ scopes: appConfig.oidcApiScopes, account }),
    );
    expect(acquireTokenPopup).not.toHaveBeenCalled();
    expect(acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it("falls back to acquireTokenPopup on InteractionRequiredAuthError, and returns its access token (AC-3)", async () => {
    const acquireTokenPopup = vi.fn().mockResolvedValue({ accessToken: "popup-token" });
    const acquireTokenRedirect = vi.fn();
    const instance = {
      getActiveAccount: vi.fn().mockReturnValue(account),
      getAllAccounts: vi.fn().mockReturnValue([account]),
      acquireTokenSilent: vi.fn().mockRejectedValue(new InteractionRequiredAuthError("interaction_required", "test-correlation-id")),
      acquireTokenPopup,
      acquireTokenRedirect,
    } as unknown as IPublicClientApplication;

    const token = await acquireApiAccessToken(instance, appConfig);

    expect(token).toBe("popup-token");
    expect(acquireTokenPopup).toHaveBeenCalledWith(
      expect.objectContaining({ scopes: appConfig.oidcApiScopes, account }),
    );
    expect(acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it("never calls acquireTokenRedirect, even when both silent and popup fail (never a redirect from inside a request)", async () => {
    const acquireTokenRedirect = vi.fn();
    const instance = {
      getActiveAccount: vi.fn().mockReturnValue(account),
      getAllAccounts: vi.fn().mockReturnValue([account]),
      acquireTokenSilent: vi.fn().mockRejectedValue(new InteractionRequiredAuthError("interaction_required", "test-correlation-id")),
      acquireTokenPopup: vi.fn().mockRejectedValue(new Error("user closed the popup")),
      acquireTokenRedirect,
    } as unknown as IPublicClientApplication;

    const token = await acquireApiAccessToken(instance, appConfig);

    expect(token).toBeNull();
    expect(acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it("resolves null (never throws) when acquireTokenSilent fails with something other than InteractionRequiredAuthError -- no popup for an unrelated failure", async () => {
    const acquireTokenPopup = vi.fn();
    const instance = {
      getActiveAccount: vi.fn().mockReturnValue(account),
      getAllAccounts: vi.fn().mockReturnValue([account]),
      acquireTokenSilent: vi.fn().mockRejectedValue(new Error("network down")),
      acquireTokenPopup,
      acquireTokenRedirect: vi.fn(),
    } as unknown as IPublicClientApplication;

    await expect(acquireApiAccessToken(instance, appConfig)).resolves.toBeNull();
    expect(acquireTokenPopup).not.toHaveBeenCalled();
  });
});
