import { afterEach, describe, expect, it, vi } from "vitest";
import { createApiClient } from "../../src/api/client";

function pdfFile(name = "contract.pdf") {
  return new File(["%PDF-1.4"], name, { type: "application/pdf" });
}

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

describe("createApiClient().uploadDocument (task E06/F05/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("POSTs multipart/form-data to <baseUrl>/api/documents with the file and X-Tenant-Id header", async () => {
    const document = {
      id: "doc-1",
      contractId: null,
      fileName: "contract.pdf",
      mimeType: "application/pdf",
      processingStatus: "Completed",
      createdAt: "2026-09-06T08:00:00Z",
    };
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(document), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);
    const file = pdfFile();

    await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", file);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/documents");
    expect(init.method).toBe("POST");
    expect(init.headers).toEqual({ "X-Tenant-Id": "tenant-1" });
    expect(init.cache).toBe("no-store");
    expect(init.body).toBeInstanceOf(FormData);
    const submittedFile = (init.body as FormData).get("file") as File;
    expect(submittedFile.name).toBe("contract.pdf");
    expect(submittedFile.type).toBe("application/pdf");
  });

  it("reports ok:true with the stored (already-processed) document on 201", async () => {
    const document = {
      id: "doc-1",
      contractId: "contract-1",
      fileName: "contract.pdf",
      mimeType: "application/pdf",
      processingStatus: "NeedsReview",
      createdAt: "2026-09-06T08:00:00Z",
    };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(document), { status: 201 })));

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", pdfFile());

    expect(result).toEqual({ ok: true, statusCode: 201, document, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify("Missing/invalid X-Tenant-Id header."), { status: 400 }),
      ),
    );

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("bad-tenant", pdfFile());

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      document: null,
      error: "Missing/invalid X-Tenant-Id header.",
    });
  });

  it("falls back to a status-based message when a non-2xx body is not JSON", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Bad Gateway", { status: 502, statusText: "Bad Gateway" })));

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", pdfFile());

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBe(502);
    expect(result.document).toBeNull();
    expect(result.error).toContain("502");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", pdfFile());

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.document).toBeNull();
    expect(result.error).toContain("https://api.dev.contigo.example/api/documents");
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getDocument (task E06/F05/US02/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const document = {
    id: "doc-1",
    contractId: "contract-1",
    fileName: "contract.pdf",
    mimeType: "application/pdf",
    documentType: "Msa",
    processingStatus: "Completed",
    createdAt: "2026-09-06T08:00:00Z",
  };

  it("GETs <baseUrl>/api/documents/{id} with the X-Tenant-Id header, no body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(document), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getDocument("tenant-1", "doc-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/documents/doc-1");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the document (including documentType) on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(document), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getDocument("tenant-1", "doc-1");

    expect(result).toEqual({ ok: true, statusCode: 200, document, error: null });
  });

  it("reports ok:false with a named error (no response body to parse) on 404", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getDocument("tenant-1", "missing-doc");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBe(404);
    expect(result.document).toBeNull();
    expect(result.error).toContain("missing-doc");
  });

  it("reports ok:false with the parsed JSON string error on 400", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify("Missing/invalid X-Tenant-Id header."), { status: 400 }),
      ),
    );

    const result = await createApiClient("https://api.dev.contigo.example").getDocument("bad-tenant", "doc-1");

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      document: null,
      error: "Missing/invalid X-Tenant-Id header.",
    });
  });

  it("falls back to a status-based message when a non-2xx body is not JSON", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Bad Gateway", { status: 502, statusText: "Bad Gateway" })));

    const result = await createApiClient("https://api.dev.contigo.example").getDocument("tenant-1", "doc-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBe(502);
    expect(result.document).toBeNull();
    expect(result.error).toContain("502");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getDocument("tenant-1", "doc-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.document).toBeNull();
    expect(result.error).toContain("https://api.dev.contigo.example/api/documents/doc-1");
    expect(result.error).toContain("network down");
  });
});
