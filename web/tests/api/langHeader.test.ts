import { afterEach, describe, expect, it, vi } from "vitest";
import { createApiClient } from "../../src/api/client";
import { setActiveLang } from "../../src/i18n/activeLang";

const json = (_input?: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }));

describe("the language on screen travels with every request", () => {
  afterEach(() => window.localStorage.clear());

  it("sends X-Raffa-Lang with the language the user chose, and follows a change", async () => {
    const fetchMock = vi.fn(json);
    const client = createApiClient("http://api.test", async () => "t", { fetch: fetchMock });

    setActiveLang("it");
    await client.getToday!("tenant-1");
    expect(new Headers(fetchMock.mock.calls[0]![1]!.headers).get("X-Raffa-Lang")).toBe("it");

    setActiveLang("en");
    await client.getRenewalsBoard!("tenant-1");
    expect(new Headers(fetchMock.mock.calls[1]![1]!.headers).get("X-Raffa-Lang")).toBe("en");
    // The other headers are untouched.
    expect(new Headers(fetchMock.mock.calls[1]![1]!.headers).get("X-Tenant-Id")).toBe("tenant-1");
  });
});
