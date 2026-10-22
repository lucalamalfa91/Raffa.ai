// In-flight GET coalescing for the API client (latency work, "the shell multiplies traffic").
//
// The shell (`AppShell`, `RailNav`) and the routed screen mount at the same instant and each fires
// its own copy of the same reads (`GET /api/workspaces`, `GET /api/documents?pageSize=1`,
// `GET /api/contracts?pageSize=100`, ...). Every one of those is a separate request the server has
// to compute and the API's 4-connection pool has to serialise. This wrapper makes identical
// *concurrent* GETs share ONE network request.
//
// What it deliberately does NOT do:
//   * It caches nothing. The shared entry is removed the moment the request settles, so a GET made
//     after that point is always a fresh request -- freshness is exactly what it was before; only
//     duplicates that were in flight at the same instant disappear.
//   * It never lets a GET read through a write. Any non-GET request through this wrapper drops every
//     in-flight entry when it starts and again when it settles, so a read issued after a write has
//     completed can never join a read that was already on the wire before that write (the
//     "PUT negotiation-steps, then read it back" pattern depends on this).
//   * It never shares a request that carries an `AbortSignal` (or any init member other than
//     `method`/`headers`/`cache`): one caller aborting must not break the other, and a request this
//     wrapper does not fully understand is passed through untouched.
//   * It never touches the request itself: `fetch` is called with the caller's own `input` and
//     `init`, and it is looked up on `globalThis` at call time, so a test's `vi.stubGlobal("fetch")`
//     keeps working however late it is installed.
//
// Each caller gets its own `Response` (`clone()`), so one caller reading `.json()` never starves
// another. When nobody joined, the caller gets the original response and nothing is cloned.

export type CoalescingFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

interface InFlightGet {
  /** Callers sharing this request, the one that started it included. Final once the entry settles. */
  sharers: number;
  response: Promise<Response>;
}

/** `init` members a coalesced request may carry; anything else (body, signal, credentials, ...) opts out. */
const COALESCIBLE_INIT_KEYS = new Set(["method", "headers", "cache"]);

function isGet(init: RequestInit | undefined): boolean {
  return init?.method === undefined || init.method.toUpperCase() === "GET";
}

/**
 * Identity of a request for sharing purposes: method (always GET here), absolute URL, `cache` mode
 * and the *effective* header set (names lower-cased and sorted, so `Authorization` and
 * `X-Tenant-Id` always take part). `null` means "do not share this one".
 */
function coalescingKey(input: RequestInfo | URL, init: RequestInit | undefined): string | null {
  if (typeof input !== "string" && !(input instanceof URL)) return null;
  if (!isGet(init)) return null;
  if (init !== undefined) {
    for (const [name, value] of Object.entries(init)) {
      if (value !== undefined && !COALESCIBLE_INIT_KEYS.has(name)) return null;
    }
  }
  let headers: [string, string][];
  try {
    headers = [...new Headers(init?.headers).entries()].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
  } catch {
    // Not a header set `Headers` understands: let `fetch` itself decide what to do with it.
    return null;
  }
  return JSON.stringify([String(input), init?.cache ?? null, headers]);
}

/**
 * One instance per API client (its entries are that client's in-flight requests; nothing is shared
 * across clients or tests).
 */
export function createCoalescingFetch(): CoalescingFetch {
  const inFlight = new Map<string, InFlightGet>();

  const network = (input: RequestInfo | URL, init: RequestInit | undefined): Promise<Response> =>
    // Resolved through a promise so a synchronous throw from `fetch` rejects like a network failure.
    new Promise<Response>((resolve) => resolve(globalThis.fetch(input, init)));

  return async function coalescingFetch(input, init) {
    if (!isGet(init)) {
      // A write: reads already on the wire may predate it, so none of them may be joined afterwards.
      inFlight.clear();
      try {
        return await network(input, init);
      } finally {
        inFlight.clear();
      }
    }

    const key = coalescingKey(input, init);
    if (key === null) return network(input, init);

    const existing = inFlight.get(key);
    if (existing !== undefined) {
      existing.sharers += 1;
      return (await existing.response).clone();
    }

    const entry: InFlightGet = {
      sharers: 1,
      // `finally` runs before any awaiting caller resumes, so by then the entry is gone from the map
      // (nobody can join after settlement) and `sharers` is final.
      response: network(input, init).finally(() => {
        if (inFlight.get(key) === entry) inFlight.delete(key);
      }),
    };
    inFlight.set(key, entry);

    const response = await entry.response;
    return entry.sharers > 1 ? response.clone() : response;
  };
}
