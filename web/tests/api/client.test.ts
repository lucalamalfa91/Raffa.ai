import { afterEach, describe, expect, it, vi } from "vitest";
import { createApiClient } from "../../src/api/client";

describe("createApiClient().getHealth", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("calls GET <baseUrl>/health without caching", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("Healthy", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getHealth();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/health");
    expect(init).toEqual({ cache: "no-store" });
  });

  it("reports ok:true with the response body on 200 Healthy", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Healthy", { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getHealth();

    expect(result).toEqual({ ok: true, statusCode: 200, body: "Healthy" });
  });

  it("reports ok:true on 200 Degraded (still a 2xx, per Program.cs's default HealthCheckOptions)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Degraded", { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getHealth();

    expect(result).toEqual({ ok: true, statusCode: 200, body: "Degraded" });
  });

  it("reports ok:false with the response body on 503 Unhealthy, without throwing", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Unhealthy", { status: 503 })));

    const result = await createApiClient("https://api.dev.contigo.example").getHealth();

    expect(result).toEqual({ ok: false, statusCode: 503, body: "Unhealthy" });
  });

  it("resolves (does not throw) with statusCode null and a descriptive body when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getHealth();

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.body).toContain("https://api.dev.contigo.example/health");
    expect(result.body).toContain("network down");
  });
});

describe("createApiClient().createWorkspace (task E06/F03/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("POSTs JSON to <baseUrl>/api/workspaces with the given name", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: "w-1", name: "Acme", createdAt: "2026-09-06T08:00:00Z" }), {
        status: 201,
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").createWorkspace({ name: "Acme" });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/workspaces");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: "Acme" }),
      cache: "no-store",
    });
  });

  it("reports ok:true with the created workspace on 201", async () => {
    const workspace = { id: "w-1", name: "Acme", createdAt: "2026-09-06T08:00:00Z" };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(workspace), { status: 201 })));

    const result = await createApiClient("https://api.dev.contigo.example").createWorkspace({ name: "Acme" });

    expect(result).toEqual({ ok: true, statusCode: 201, workspace, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("A workspace 'name' is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").createWorkspace({ name: "" });

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      workspace: null,
      error: "A workspace 'name' is required.",
    });
  });

  it("falls back to a status-based message when a non-2xx body is not JSON", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Bad Gateway", { status: 502, statusText: "Bad Gateway" })));

    const result = await createApiClient("https://api.dev.contigo.example").createWorkspace({ name: "Acme" });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBe(502);
    expect(result.workspace).toBeNull();
    expect(result.error).toContain("502");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").createWorkspace({ name: "Acme" });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.workspace).toBeNull();
    expect(result.error).toContain("https://api.dev.contigo.example/api/workspaces");
    expect(result.error).toContain("network down");
  });
});
