/**
 * AskRaffa 4.0 fixture mode, client half (IMPLEMENTATION-PLAN.md §2 "Fixture mode"). Test/demo only:
 * active only when the runtime config says `fixtures: true` AND the URL carries `?fixture=zero|full`
 * (or this tab already remembered a scenario in `sessionStorage['raffa4.mode']`). Then the app does
 * not construct MSAL at all; every API request carries `X-Raffa-Fixture-User` (the viewer,
 * `sessionStorage['raffa4.viewer']`, default luca) and `X-Raffa-Now` (`?now=`, default 2026-09-08),
 * which the API honours only when its own `Fixtures:Enabled` is on.
 */

export type FixtureScenario = "zero" | "full";
export type FixtureViewer = "luca" | "marta";

export const MODE_STORAGE_KEY = "raffa4.mode";
export const VIEWER_STORAGE_KEY = "raffa4.viewer";
export const NOW_STORAGE_KEY = "raffa4.now";
export const DEFAULT_FIXTURE_NOW = "2026-09-08";
export const FIXTURE_USER_HEADER = "X-Raffa-Fixture-User";
export const FIXTURE_NOW_HEADER = "X-Raffa-Now";

export interface FixtureBoot {
  scenario: FixtureScenario;
  viewer: FixtureViewer;
  /** YYYY-MM-DD */
  now: string;
}

export const isScenario = (value: unknown): value is FixtureScenario => value === "zero" || value === "full";
export const isViewer = (value: unknown): value is FixtureViewer => value === "luca" || value === "marta";
const isIsoDate = (value: unknown): value is string => typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value);

function read(key: string): string | null {
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

export function remember(key: string, value: string): void {
  try {
    window.sessionStorage.setItem(key, value);
  } catch {
    // Private mode: the choice lasts for this page only.
  }
}

/**
 * The fixture session to boot into, or `null` for the normal (MSAL) app. Never active unless the
 * runtime config enables fixtures. URL values win and are remembered for the rest of the tab.
 */
export function readFixtureBoot(configFixtures: boolean | undefined, search: string): FixtureBoot | null {
  if (configFixtures !== true) return null;
  const params = new URLSearchParams(search);
  const requested = params.get("fixture");
  const scenario = isScenario(requested) ? requested : read(MODE_STORAGE_KEY);
  if (!isScenario(scenario)) return null;

  const requestedNow = params.get("now");
  const now = isIsoDate(requestedNow) ? requestedNow : (read(NOW_STORAGE_KEY) ?? DEFAULT_FIXTURE_NOW);
  const storedViewer = read(VIEWER_STORAGE_KEY);
  const viewer: FixtureViewer = isViewer(storedViewer) ? storedViewer : "luca";

  remember(MODE_STORAGE_KEY, scenario);
  remember(NOW_STORAGE_KEY, isIsoDate(now) ? now : DEFAULT_FIXTURE_NOW);
  remember(VIEWER_STORAGE_KEY, viewer);
  return { scenario, viewer, now: isIsoDate(now) ? now : DEFAULT_FIXTURE_NOW };
}

/**
 * A `fetch` that adds the two fixture headers to every request bound for the API origin (and only
 * those). `headers()` is read per request, so switching the viewer applies to the next call.
 */
export function createFixtureFetch(
  apiBaseUrl: string,
  headers: () => Record<string, string>,
  base: typeof fetch = (input, init) => globalThis.fetch(input, init),
): typeof fetch {
  const apiOrigin = new URL(apiBaseUrl).origin;
  return (input, init) => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    let sameApi = false;
    try {
      sameApi = new URL(url, window.location.href).origin === apiOrigin;
    } catch {
      sameApi = false;
    }
    if (!sameApi) return base(input, init);
    const merged = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
    for (const [name, value] of Object.entries(headers())) merged.set(name, value);
    return base(input, { ...init, headers: merged });
  };
}

export interface FixtureSessionBody {
  tenantId: string;
  tenantName: string;
  scenario: FixtureScenario | null;
  now: string;
}

/** `POST /api/fixtures/reset` — wipes and reseeds the fixture tenant. */
export async function resetFixtureTenant(apiBaseUrl: string, scenario: FixtureScenario, fetchImpl: typeof fetch): Promise<FixtureSessionBody> {
  const response = await fetchImpl(new URL("/api/fixtures/reset", apiBaseUrl), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ scenario }),
  });
  if (!response.ok) {
    throw new Error(`Fixture reset failed: HTTP ${response.status}`);
  }
  return (await response.json()) as FixtureSessionBody;
}

const IT_MONTHS = ["Gen", "Feb", "Mar", "Apr", "Mag", "Giu", "Lug", "Ago", "Set", "Ott", "Nov", "Dic"];

/** "2026-09-08" → "08 Set 2026" (the demo bar is Italian in both languages, as in the prototype). */
export function formatItalianDate(iso: string): string {
  const [y, m, d] = iso.split("-");
  const month = IT_MONTHS[Number(m) - 1] ?? m;
  return `${d} ${month} ${y}`;
}
