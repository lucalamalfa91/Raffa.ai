import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createFixtureFetch,
  formatItalianDate,
  readFixtureBoot,
  resetFixtureTenant,
} from "../../src/fixtures/fixtureMode";

afterEach(() => window.sessionStorage.clear());

describe("readFixtureBoot", () => {
  it("is never active when the runtime config does not enable fixtures", () => {
    expect(readFixtureBoot(undefined, "?fixture=zero")).toBeNull();
    expect(readFixtureBoot(false, "?fixture=full")).toBeNull();
  });

  it("needs a scenario in the URL or remembered in the tab", () => {
    expect(readFixtureBoot(true, "")).toBeNull();
    expect(readFixtureBoot(true, "?fixture=other")).toBeNull();
  });

  it("reads ?fixture and ?now, defaults the viewer to luca and remembers all three", () => {
    expect(readFixtureBoot(true, "?fixture=full&now=2026-10-01")).toEqual({ scenario: "full", viewer: "luca", now: "2026-10-01" });
    expect(window.sessionStorage.getItem("raffa4.mode")).toBe("full");
    // A later navigation without query parameters keeps the session.
    expect(readFixtureBoot(true, "")).toEqual({ scenario: "full", viewer: "luca", now: "2026-10-01" });
  });

  it("defaults the clock to 2026-09-08 and keeps a remembered viewer", () => {
    window.sessionStorage.setItem("raffa4.viewer", "marta");
    expect(readFixtureBoot(true, "?fixture=zero&now=bad")).toEqual({ scenario: "zero", viewer: "marta", now: "2026-09-08" });
  });
});

describe("createFixtureFetch", () => {
  it("adds the fixture headers to API requests only, read per request", async () => {
    const base = vi.fn().mockResolvedValue(new Response("{}"));
    let viewer = "luca";
    const fixtureFetch = createFixtureFetch("http://api.test", () => ({ "X-Raffa-Fixture-User": viewer, "X-Raffa-Now": "2026-09-08" }), base);

    await fixtureFetch(new URL("/api/workspaces", "http://api.test"), { headers: { "X-Tenant-Id": "t" } });
    viewer = "marta";
    await fixtureFetch("http://api.test/api/contracts");
    await fixtureFetch("https://fonts.example/archivo.css");

    const headersOf = (call: number) => new Headers((base.mock.calls[call]![1] as RequestInit | undefined)?.headers);
    expect(headersOf(0).get("x-raffa-fixture-user")).toBe("luca");
    expect(headersOf(0).get("x-tenant-id")).toBe("t");
    expect(headersOf(1).get("x-raffa-fixture-user")).toBe("marta");
    expect(headersOf(1).get("x-raffa-now")).toBe("2026-09-08");
    expect(headersOf(2).get("x-raffa-fixture-user")).toBeNull();
  });
});

describe("resetFixtureTenant", () => {
  it("posts the scenario and returns the session", async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(JSON.stringify({ tenantId: "t", tenantName: "Helvetia Foods AG", scenario: "zero", now: "2026-09-08" })));
    const session = await resetFixtureTenant("http://api.test", "zero", fetchImpl);

    expect(session.tenantName).toBe("Helvetia Foods AG");
    const [url, init] = fetchImpl.mock.calls[0]!;
    expect(String(url)).toBe("http://api.test/api/fixtures/reset");
    expect(init).toMatchObject({ method: "POST", body: JSON.stringify({ scenario: "zero" }) });
  });

  it("throws on a failed reset", async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response("", { status: 404 }));
    await expect(resetFixtureTenant("http://api.test", "full", fetchImpl)).rejects.toThrow(/404/);
  });
});

it("formats the demo bar clock in Italian", () => {
  expect(formatItalianDate("2026-09-08")).toBe("08 Set 2026");
  expect(formatItalianDate("2026-10-18")).toBe("18 Ott 2026");
});
