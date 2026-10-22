import { afterEach, describe, expect, it, vi } from "vitest";
import { createApiClient } from "../../src/api/client";

// Latency work: the shell and the routed screen fire the same reads at the same instant. Identical
// concurrent GETs of one client share one network request (src/api/coalescingFetch.ts); nothing is
// cached once a request settles, and a write drops whatever is in flight.

const BASE = "https://api.dev.raffa.example";
const page = { items: [], page: 1, pageSize: 1, totalCount: 0, counts: {} };

describe("createApiClient: identical concurrent GETs share one request", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("two concurrent listDocuments with the same tenant, query and token make ONE fetch; both results are complete", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify(page), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createApiClient(BASE, async () => "test-access-token");

    const [first, second] = await Promise.all([
      client.listDocuments("tenant-1", { pageSize: 1 }),
      client.listDocuments("tenant-1", { pageSize: 1 }),
    ]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(first).toEqual({ ok: true, statusCode: 200, page, error: null });
    expect(second).toEqual({ ok: true, statusCode: 200, page, error: null });
    const [, init] = fetchMock.mock.calls[0];
    expect(init).toEqual({
      headers: { "X-Tenant-Id": "tenant-1", Authorization: "Bearer test-access-token" },
      cache: "no-store",
    });
  });

  it("a different tenant or query is a different request", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify(page), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createApiClient(BASE, async () => "test-access-token");

    await Promise.all([
      client.listDocuments("tenant-1", { pageSize: 1 }),
      client.listDocuments("tenant-2", { pageSize: 1 }),
      client.listDocuments("tenant-1", { pageSize: 2 }),
    ]);

    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("a different token is a different request", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify(page), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    let token = "token-1";
    const client = createApiClient(BASE, async () => token);

    const first = client.listDocuments("tenant-1"); // reads "token-1" as it starts
    token = "token-2";
    const second = client.listDocuments("tenant-1");
    await Promise.all([first, second]);

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("sequential identical reads are NOT merged: nothing is cached once a request settles", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify(page), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createApiClient(BASE);

    await client.listDocuments("tenant-1", { pageSize: 1 });
    await client.listDocuments("tenant-1", { pageSize: 1 });

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("every concurrent caller still maps a non-2xx status to its own error result", async () => {
    const fetchMock = vi
      .fn()
      .mockImplementation(async () => new Response(JSON.stringify("Missing/invalid X-Tenant-Id header."), { status: 400 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createApiClient(BASE);

    const results = await Promise.all([client.listDocuments("tenant-1"), client.listDocuments("tenant-1")]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    for (const result of results) {
      expect(result).toEqual({ ok: false, statusCode: 400, page: null, error: "Missing/invalid X-Tenant-Id header." });
    }
  });

  it("every concurrent caller gets the network-failure result when the shared request fails", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("network down"));
    vi.stubGlobal("fetch", fetchMock);
    const client = createApiClient(BASE);

    const results = await Promise.all([client.listWorkspaces(), client.listWorkspaces()]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    for (const result of results) {
      expect(result.ok).toBe(false);
      expect(result.statusCode).toBeNull();
      expect(result.error).toContain("network down");
    }
  });

  it("a read issued after a write completes is a fresh request, never the read that predates the write", async () => {
    let steps: string[] = [];
    const fetchMock = vi.fn().mockImplementation(async (_url: URL, init: RequestInit) => {
      if (init.method === "PUT") {
        steps = (JSON.parse(String(init.body)) as { steps: string[] }).steps;
        return new Response(JSON.stringify(steps), { status: 200 });
      }
      const snapshot = [...steps]; // what the server holds when this GET is processed
      await new Promise((resolve) => setTimeout(resolve, 20));
      return new Response(JSON.stringify(snapshot), { status: 200 });
    });
    vi.stubGlobal("fetch", fetchMock);
    const client = createApiClient(BASE);

    const poll = client.getNegotiationSteps("tenant-1", "contract-1"); // on the wire before the write
    await client.putNegotiationSteps("tenant-1", "contract-1", { steps: ["call-supplier"] });
    const readBack = await client.getNegotiationSteps("tenant-1", "contract-1");

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(readBack.steps).toEqual(["call-supplier"]);
    expect((await poll).steps).toEqual([]);
  });

  it("two clients never share a request", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify(page), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await Promise.all([createApiClient(BASE).listDocuments("tenant-1"), createApiClient(BASE).listDocuments("tenant-1")]);

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
