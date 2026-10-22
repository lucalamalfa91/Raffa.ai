import { afterEach, describe, expect, it, vi } from "vitest";
import { createCoalescingFetch } from "../../src/api/coalescingFetch";

/** A fetch whose responses the test settles by hand, so "in flight" is under the test's control. */
function controllableFetch() {
  const pending: Array<{
    input: RequestInfo | URL;
    init: RequestInit | undefined;
    resolve: (response: Response) => void;
    reject: (reason: unknown) => void;
  }> = [];
  const fetchMock = vi.fn(
    (input: RequestInfo | URL, init?: RequestInit) =>
      new Promise<Response>((resolve, reject) => {
        pending.push({ input, init, resolve, reject });
      }),
  );
  vi.stubGlobal("fetch", fetchMock);
  return { fetchMock, pending };
}

const URL_A = "https://api.dev.raffa.example/api/workspaces";
const GET_INIT = { headers: { "X-Tenant-Id": "t-1", Authorization: "Bearer a" }, cache: "no-store" as const };

describe("createCoalescingFetch", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shares ONE network request between identical GETs in flight, and gives each caller its own readable Response", async () => {
    const { fetchMock, pending } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    const first = apiFetch(URL_A, GET_INIT);
    const second = apiFetch(URL_A, { ...GET_INIT, headers: { ...GET_INIT.headers } });
    const third = apiFetch(new URL(URL_A), GET_INIT);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    pending[0].resolve(new Response(JSON.stringify({ workspaces: [1, 2, 3] }), { status: 200 }));

    const responses = await Promise.all([first, second, third]);
    expect(new Set(responses).size).toBe(3);
    for (const response of responses) {
      expect(response.status).toBe(200);
      expect(await response.json()).toEqual({ workspaces: [1, 2, 3] });
    }
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("hands a lone caller the original Response and passes input and init through untouched", async () => {
    const { fetchMock, pending } = controllableFetch();
    const apiFetch = createCoalescingFetch();
    const original = new Response("ok", { status: 200 });

    const call = apiFetch(URL_A, GET_INIT);
    pending[0].resolve(original);

    expect(await call).toBe(original);
    const [input, init] = fetchMock.mock.calls[0];
    expect(input).toBe(URL_A);
    expect(init).toBe(GET_INIT);
  });

  it("caches nothing: a GET made after the first one settled is a new network request", async () => {
    const { fetchMock, pending } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    const first = apiFetch(URL_A, GET_INIT);
    pending[0].resolve(new Response("one", { status: 200 }));
    expect(await (await first).text()).toBe("one");

    const second = apiFetch(URL_A, GET_INIT);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    pending[1].resolve(new Response("two", { status: 200 }));
    expect(await (await second).text()).toBe("two");
  });

  it("does not share requests that differ in URL, query, Authorization or X-Tenant-Id", async () => {
    const { fetchMock } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    void apiFetch(URL_A, GET_INIT);
    void apiFetch(`${URL_A}?pageSize=1`, GET_INIT);
    void apiFetch(URL_A, { ...GET_INIT, headers: { ...GET_INIT.headers, Authorization: "Bearer b" } });
    void apiFetch(URL_A, { ...GET_INIT, headers: { ...GET_INIT.headers, "X-Tenant-Id": "t-2" } });
    void apiFetch(URL_A, { headers: GET_INIT.headers });

    expect(fetchMock).toHaveBeenCalledTimes(5);
  });

  it("treats header names case-insensitively and ignores their order", async () => {
    const { fetchMock } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    void apiFetch(URL_A, { headers: { "x-tenant-id": "t-1", authorization: "Bearer a" }, cache: "no-store" });
    void apiFetch(URL_A, { headers: { Authorization: "Bearer a", "X-Tenant-Id": "t-1" }, cache: "no-store" });

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("never shares a request that carries an AbortSignal: aborting one caller cannot break another", async () => {
    const { fetchMock } = controllableFetch();
    const apiFetch = createCoalescingFetch();
    const controller = new AbortController();

    void apiFetch(URL_A, { ...GET_INIT, signal: controller.signal });
    void apiFetch(URL_A, { ...GET_INIT, signal: controller.signal });
    void apiFetch(URL_A, GET_INIT);

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[0][1]?.signal).toBe(controller.signal);
  });

  it("never shares writes", async () => {
    const { fetchMock } = controllableFetch();
    const apiFetch = createCoalescingFetch();
    const post = { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}", cache: "no-store" as const };

    void apiFetch(URL_A, post);
    void apiFetch(URL_A, post);
    void apiFetch(URL_A, { method: "DELETE", cache: "no-store" });
    void apiFetch(URL_A, { method: "DELETE", cache: "no-store" });

    expect(fetchMock).toHaveBeenCalledTimes(4);
  });

  it("an explicit GET method shares with an implicit one", async () => {
    const { fetchMock } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    void apiFetch(URL_A, { method: "GET", headers: GET_INIT.headers, cache: "no-store" });
    void apiFetch(URL_A, { headers: GET_INIT.headers, cache: "no-store" });

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("a write drops what is in flight: a read issued after the write cannot join a read that predates it", async () => {
    const { fetchMock, pending } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    const before = apiFetch(URL_A, GET_INIT); // on the wire before the write
    const write = apiFetch(`${URL_A}/steps`, { method: "PUT", body: "{}", cache: "no-store" });
    expect(fetchMock).toHaveBeenCalledTimes(2);

    pending[1].resolve(new Response(null, { status: 204 }));
    await write;

    const after = apiFetch(URL_A, GET_INIT); // write settled: must be a fresh request
    expect(fetchMock).toHaveBeenCalledTimes(3);

    pending[0].resolve(new Response("stale", { status: 200 }));
    pending[2].resolve(new Response("fresh", { status: 200 }));
    expect(await (await before).text()).toBe("stale");
    expect(await (await after).text()).toBe("fresh");
  });

  it("a failed write drops what is in flight too", async () => {
    const { fetchMock, pending } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    void apiFetch(URL_A, GET_INIT);
    const write = apiFetch(`${URL_A}/steps`, { method: "PUT", body: "{}", cache: "no-store" });
    pending[1].reject(new TypeError("network down"));
    await expect(write).rejects.toThrow("network down");

    void apiFetch(URL_A, GET_INIT);
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("shares a network failure with every caller, then forgets it", async () => {
    const { fetchMock, pending } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    const first = apiFetch(URL_A, GET_INIT);
    const second = apiFetch(URL_A, GET_INIT);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    pending[0].reject(new TypeError("network down"));
    await expect(first).rejects.toThrow("network down");
    await expect(second).rejects.toThrow("network down");

    const retry = apiFetch(URL_A, GET_INIT);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    pending[1].resolve(new Response("back", { status: 200 }));
    expect(await (await retry).text()).toBe("back");
  });

  it("turns a synchronous throw from fetch into a rejection and does not keep the entry", async () => {
    const fetchMock = vi.fn(() => {
      throw new TypeError("boom");
    });
    vi.stubGlobal("fetch", fetchMock);
    const apiFetch = createCoalescingFetch();

    await expect(apiFetch(URL_A, GET_INIT)).rejects.toThrow("boom");
    await expect(apiFetch(URL_A, GET_INIT)).rejects.toThrow("boom");
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("shares non-2xx responses as they are (status and body reach every caller)", async () => {
    const { pending } = controllableFetch();
    const apiFetch = createCoalescingFetch();

    const first = apiFetch(URL_A, GET_INIT);
    const second = apiFetch(URL_A, GET_INIT);
    pending[0].resolve(new Response(JSON.stringify("nope"), { status: 400, statusText: "Bad Request" }));

    for (const response of await Promise.all([first, second])) {
      expect(response.status).toBe(400);
      expect(response.statusText).toBe("Bad Request");
      expect(await response.json()).toBe("nope");
    }
  });

  it("resolves the global fetch at call time, so a later stub is honoured", async () => {
    const apiFetch = createCoalescingFetch();
    const late = vi.fn().mockResolvedValue(new Response("late", { status: 200 }));
    vi.stubGlobal("fetch", late);

    expect(await (await apiFetch(URL_A, GET_INIT)).text()).toBe("late");
    expect(late).toHaveBeenCalledTimes(1);
  });

  it("keeps instances apart: nothing is shared between two coalescers", async () => {
    const { fetchMock } = controllableFetch();

    void createCoalescingFetch()(URL_A, GET_INIT);
    void createCoalescingFetch()(URL_A, GET_INIT);

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
