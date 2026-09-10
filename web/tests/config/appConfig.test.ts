import { afterEach, describe, expect, it, vi } from "vitest";
import { AppConfigError, loadAppConfig, validateAppConfig } from "../../src/config/appConfig";

const validConfig = {
  apiBaseUrl: "https://api.dev.raffa.example",
  oidcAuthority: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000",
  oidcClientId: "11111111-1111-1111-1111-111111111111",
  oidcRedirectUri: "https://web.dev.raffa.example",
  oidcApiScopes: [
    "api://11111111-1111-1111-1111-111111111111/Raffa.Read",
    "api://11111111-1111-1111-1111-111111111111/Raffa.Write",
  ],
};

describe("validateAppConfig", () => {
  it("accepts a well-formed config and returns exactly the documented fields", () => {
    expect(validateAppConfig(validConfig)).toEqual(validConfig);
  });

  it("ignores unknown extra fields (e.g. a documentation comment) instead of rejecting them", () => {
    expect(validateAppConfig({ ...validConfig, _comment: "placeholder" })).toEqual(validConfig);
  });

  it.each(["apiBaseUrl", "oidcAuthority", "oidcClientId", "oidcRedirectUri"] as const)(
    "rejects a config missing required field %s",
    (field) => {
      const rest = { ...validConfig };
      delete (rest as Record<string, unknown>)[field];
      expect(() => validateAppConfig(rest)).toThrow(AppConfigError);
    },
  );

  it.each(["apiBaseUrl", "oidcAuthority", "oidcClientId", "oidcRedirectUri"] as const)(
    "rejects a config with an empty-string %s",
    (field) => {
      expect(() => validateAppConfig({ ...validConfig, [field]: "   " })).toThrow(AppConfigError);
    },
  );

  it("rejects a config with no OIDC API scopes", () => {
    expect(() => validateAppConfig({ ...validConfig, oidcApiScopes: [] })).toThrow(AppConfigError);
  });

  it("rejects a config whose scopes are not all non-empty strings", () => {
    expect(() => validateAppConfig({ ...validConfig, oidcApiScopes: ["ok", ""] })).toThrow(AppConfigError);
  });

  it("rejects a non-object payload", () => {
    expect(() => validateAppConfig(null)).toThrow(AppConfigError);
    expect(() => validateAppConfig("not-an-object")).toThrow(AppConfigError);
    expect(() => validateAppConfig(["array", "not", "object"])).toThrow(AppConfigError);
  });
});

describe("loadAppConfig", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("fetches /config.json by default, without caching, and validates it", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(validConfig), { status: 200 }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const config = await loadAppConfig();

    expect(config).toEqual(validConfig);
    expect(fetchMock).toHaveBeenCalledWith("/config.json", expect.objectContaining({ cache: "no-store" }));
  });

  it("throws AppConfigError on a non-OK HTTP response, e.g. the file is missing", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("not found", { status: 404 })));
    await expect(loadAppConfig()).rejects.toThrow(AppConfigError);
  });

  it("throws AppConfigError when the response body is not valid JSON", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("<not json>", { status: 200 })));
    await expect(loadAppConfig()).rejects.toThrow(AppConfigError);
  });

  it("throws AppConfigError when the network request itself fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));
    await expect(loadAppConfig()).rejects.toThrow(AppConfigError);
  });
});
