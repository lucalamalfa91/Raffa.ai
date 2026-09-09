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

    expect(result).toEqual({ ok: true, statusCode: 201, document, rejection: null, error: null });
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
      rejection: null,
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

describe("createApiClient().uploadDocument -- 413/415/422 (task E13/F04/US01/T01, documented ahead of the backend landing)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports the structured 422 admission-gate rejection on `rejection`, leaving `error` null", async () => {
    const rejectionBody = { rejected: true, detectedType: "Other", confidence: 0.93, reason: "not_a_contract", hint: "..." };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(rejectionBody), { status: 422 })));

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", pdfFile());

    expect(result).toEqual({ ok: false, statusCode: 422, document: null, rejection: rejectionBody, error: null });
  });

  it("reports a plain-string 413 (oversized) on `error`, leaving `rejection` null", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("Contigo accepts files up to 50 MB. This file is larger."), { status: 413 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", pdfFile());

    expect(result).toEqual({
      ok: false,
      statusCode: 413,
      document: null,
      rejection: null,
      error: "Contigo accepts files up to 50 MB. This file is larger.",
    });
  });

  it("reports a plain-string 415 (format) on `error`, leaving `rejection` null", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("Contigo reads PDF, Word, Excel and scanned images"), { status: 415 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", pdfFile());

    expect(result).toEqual({
      ok: false,
      statusCode: 415,
      document: null,
      rejection: null,
      error: "Contigo reads PDF, Word, Excel and scanned images",
    });
  });

  it("still reports rejection:null on a 201 (existing 201 path unaffected by the new field)", async () => {
    const document = {
      id: "doc-1",
      contractId: null,
      fileName: "contract.pdf",
      mimeType: "application/pdf",
      processingStatus: "Completed",
      createdAt: "2026-09-06T08:00:00Z",
    };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(document), { status: 201 })));

    const result = await createApiClient("https://api.dev.contigo.example").uploadDocument("tenant-1", pdfFile());

    expect(result).toEqual({ ok: true, statusCode: 201, document, rejection: null, error: null });
  });
});

describe("createApiClient().listDocuments (task E13/F09/US01/T03, documented ahead of the backend landing)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const documentListPage = {
    items: [
      {
        id: "doc-1",
        contractId: "contract-1",
        supplierName: "Salesforce",
        fileName: "Salesforce_MSA.pdf",
        documentType: "Msa",
        processingStatus: "NeedsReview",
        stage: null,
        pageCount: 12,
        createdAt: "2026-09-06T08:00:00Z",
        weakFactCount: 2,
      },
    ],
    page: 1,
    pageSize: 25,
    totalCount: 1,
  };

  it("GETs <baseUrl>/api/documents with the X-Tenant-Id header, no query parameters, when called with no query", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(documentListPage), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").listDocuments("tenant-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/documents");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("serializes status/page/pageSize as query parameters when supplied", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(documentListPage), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").listDocuments("tenant-1", {
      status: "NeedsReview",
      page: 2,
      pageSize: 50,
    });

    const [url] = fetchMock.mock.calls[0];
    const params = new URL(String(url)).searchParams;
    expect(params.get("status")).toBe("NeedsReview");
    expect(params.get("page")).toBe("2");
    expect(params.get("pageSize")).toBe("50");
  });

  it("reports ok:true with the page on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(documentListPage), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").listDocuments("tenant-1");

    expect(result).toEqual({ ok: true, statusCode: 200, page: documentListPage, error: null });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").listDocuments("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.page).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getDocumentPreviewUrl (task E13/F09/US01/T03, documented ahead of the backend landing)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("GETs <baseUrl>/api/documents/{id}/preview with the X-Tenant-Id header and turns a 200 PNG into an object URL", async () => {
    const png = new Blob([new Uint8Array([1, 2, 3])], { type: "image/png" });
    const fetchMock = vi.fn().mockResolvedValue(new Response(png, { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const createObjectURL = vi.fn().mockReturnValue("blob:mock-url");
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL }));

    const result = await createApiClient("https://api.dev.contigo.example").getDocumentPreviewUrl("tenant-1", "doc-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/documents/doc-1/preview");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
    expect(result).toEqual({ ok: true, statusCode: 200, objectUrl: "blob:mock-url", error: null });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getDocumentPreviewUrl("tenant-1", "missing-doc");

    expect(result).toEqual({ ok: false, statusCode: 404, objectUrl: null, error: "No document found for id missing-doc." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getDocumentPreviewUrl("tenant-1", "doc-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.objectUrl).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().reprocessDocument (task E13/F09/US01/T03, documented ahead of the backend landing)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const summary = {
    documentId: "doc-1",
    contractId: "contract-1",
    documentType: "Msa",
    processingStatus: "Completed",
    pagesParsed: 12,
    chunksIndexed: 12,
  };

  it("POSTs <baseUrl>/api/documents/{id}/reprocess with the X-Tenant-Id header, no body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(summary), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").reprocessDocument("tenant-1", "doc-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/documents/doc-1/reprocess");
    expect(init).toEqual({ method: "POST", headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the summary on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(summary), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").reprocessDocument("tenant-1", "doc-1");

    expect(result).toEqual({ ok: true, statusCode: 200, summary, error: null });
  });

  it("reports a named 403 when the caller is not Admin", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 403 })));

    const result = await createApiClient("https://api.dev.contigo.example").reprocessDocument("tenant-1", "doc-1");

    expect(result).toEqual({ ok: false, statusCode: 403, summary: null, error: "Only a Workspace Admin can reprocess a document." });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").reprocessDocument("tenant-1", "missing-doc");

    expect(result).toEqual({ ok: false, statusCode: 404, summary: null, error: "No document found for id missing-doc." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").reprocessDocument("tenant-1", "doc-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().deleteDocument (task E13/F09/US01/T03, documented ahead of the backend landing)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("DELETEs <baseUrl>/api/documents/{id} with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").deleteDocument("tenant-1", "doc-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/documents/doc-1");
    expect(init).toEqual({ method: "DELETE", headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true on 204", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    const result = await createApiClient("https://api.dev.contigo.example").deleteDocument("tenant-1", "doc-1");

    expect(result).toEqual({ ok: true, statusCode: 204, error: null });
  });

  it("reports a named 403 when the caller is not Admin (R-DOC-10: Procurement gets 403)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 403 })));

    const result = await createApiClient("https://api.dev.contigo.example").deleteDocument("tenant-1", "doc-1");

    expect(result).toEqual({ ok: false, statusCode: 403, error: "Only a Workspace Admin can delete a document." });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").deleteDocument("tenant-1", "missing-doc");

    expect(result).toEqual({ ok: false, statusCode: 404, error: "No document found for id missing-doc." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").deleteDocument("tenant-1", "doc-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getPortfolio (task E07/F01/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const portfolioPage = {
    items: [
      {
        contractId: "contract-1",
        supplierId: "supplier-1",
        type: "Msa",
        annualSpend: 120000,
        startDate: "2025-01-01",
        endDate: "2026-01-01",
        renewalDate: "2026-01-01",
        cancellationDeadline: "2025-11-01",
        autoRenewal: true,
        status: "active",
        risk: "High",
      },
    ],
    page: 1,
    pageSize: 25,
    totalCount: 1,
  };

  it("GETs <baseUrl>/api/contracts with the X-Tenant-Id header, no query parameters, when called with no query", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(portfolioPage), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getPortfolio("tenant-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/contracts");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("serializes every supplied filter/page field as a query parameter, and omits anything not supplied", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(portfolioPage), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getPortfolio("tenant-1", {
      supplierId: "supplier-1",
      status: "active",
      risk: "High",
      autoRenewal: true,
      minAnnualSpend: 1000,
      maxAnnualSpend: 500000,
      renewalFrom: "2026-01-01",
      renewalTo: "2026-04-30",
      page: 2,
      pageSize: 50,
    });

    const [url] = fetchMock.mock.calls[0];
    const params = new URL(String(url)).searchParams;
    expect(params.get("supplierId")).toBe("supplier-1");
    expect(params.get("status")).toBe("active");
    expect(params.get("risk")).toBe("High");
    expect(params.get("autoRenewal")).toBe("true");
    expect(params.get("minAnnualSpend")).toBe("1000");
    expect(params.get("maxAnnualSpend")).toBe("500000");
    expect(params.get("renewalFrom")).toBe("2026-01-01");
    expect(params.get("renewalTo")).toBe("2026-04-30");
    expect(params.get("page")).toBe("2");
    expect(params.get("pageSize")).toBe("50");
  });

  it("reports ok:true with the portfolio page on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(portfolioPage), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getPortfolio("tenant-1");

    expect(result).toEqual({ ok: true, statusCode: 200, portfolio: portfolioPage, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (malformed filter/page parameter)", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("'risk' must be one of Low, Medium, High, Critical."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").getPortfolio("tenant-1");

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      portfolio: null,
      error: "'risk' must be one of Low, Medium, High, Critical.",
    });
  });

  it("reports ok:false with a status-based message on a 503 (AC-4 error state), without throwing", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Service Unavailable", { status: 503, statusText: "Service Unavailable" })));

    const result = await createApiClient("https://api.dev.contigo.example").getPortfolio("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBe(503);
    expect(result.portfolio).toBeNull();
    expect(result.error).toContain("503");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getPortfolio("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.portfolio).toBeNull();
    expect(result.error).toContain("https://api.dev.contigo.example/api/contracts");
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getContract360 (task E07/F02/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const contract360Body = {
    contractId: "contract-1",
    header: {
      contractId: "contract-1",
      supplierId: null,
      type: "Msa",
      status: "active",
      annualSpend: 500000,
      totalContractValue: 1500000,
      startDate: "2025-01-01",
      endDate: "2026-01-01",
      renewalDate: "2026-01-01",
      cancellationDeadline: "2025-11-17",
      autoRenewal: true,
      risk: "High",
    },
    tabs: {
      overview: {
        currency: "CHF",
        effectiveDate: null,
        renewalTermMonths: null,
        paymentTerms: null,
        governingLaw: null,
        parentContractId: null,
        version: 1,
        createdAt: "2025-01-01T00:00:00Z",
      },
      commercials: {
        annualSpend: 500000,
        totalContractValue: 1500000,
        currency: "CHF",
        paymentTerms: null,
        autoRenewal: true,
        renewalTermMonths: null,
        lineItemCount: 0,
        lineItemAnnualCostTotal: null,
        lineItemTotalCostTotal: null,
      },
      products: [],
      clauses: [],
      obligations: [],
      risks: [],
      documents: [],
      benchmark: [],
      renewal: { endDate: "2026-01-01", renewalDate: "2026-01-01", cancellationDeadline: "2025-11-17", autoRenewal: true, renewalTermMonths: null },
      activity: [],
    },
  };

  it("GETs <baseUrl>/api/contracts/{id} with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(contract360Body), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getContract360("tenant-1", "contract-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/contracts/contract-1");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the full aggregate on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(contract360Body), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getContract360("tenant-1", "contract-1");

    expect(result).toEqual({ ok: true, statusCode: 200, contract: contract360Body, error: null });
  });

  it("reports a named 404 (no such contract for this tenant) without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getContract360("tenant-1", "missing-contract");

    expect(result).toEqual({ ok: false, statusCode: 404, contract: null, error: "No contract found for id missing-contract." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getContract360("tenant-1", "contract-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.contract).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getRenewals (task E07/F02/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const renewalsPage = {
    items: [
      {
        contractId: "contract-1",
        supplierId: null,
        status: "Determined",
        renewalDate: "2026-01-01",
        daysUntilRenewal: 30,
        annualSpend: 500000,
        cancellationDeadline: "2025-11-17",
        daysUntilCancellationDeadline: 14,
        autoRenewal: true,
        action: "Start renewal negotiation now",
        insightCard: {
          facts: {
            supplierId: null,
            renewalDate: "2026-01-01",
            daysUntilRenewal: 30,
            annualSpend: 500000,
            cancellationDeadline: "2025-11-17",
            daysUntilCancellationDeadline: 14,
          },
          recommendations: {
            recommendedAction: "Start renewal negotiation now",
            explanation: "Renews in 30 days.",
            annualUpliftPercent: null,
            marketPosition: null,
            potentialSavingsRange: null,
          },
        },
      },
    ],
    totalCount: 1,
  };

  it("GETs <baseUrl>/api/renewals with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(renewalsPage), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getRenewals("tenant-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/renewals");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the pipeline page on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(renewalsPage), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getRenewals("tenant-1");

    expect(result).toEqual({ ok: true, statusCode: 200, renewals: renewalsPage, error: null });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getRenewals("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.renewals).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getRenewalPriority (task E07/F02/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const priorityBody = {
    contractId: "contract-1",
    totalScore: 72,
    components: {
      spendWeight: { score: 20, explanation: "..." },
      timeUrgency: { score: 20, explanation: "..." },
      benchmarkOpportunity: { score: 10, explanation: "..." },
      priceIncreaseRisk: { score: 7, explanation: "..." },
      contractRisk: { score: 15, explanation: "..." },
    },
  };

  it("GETs <baseUrl>/api/renewals/{contractId}/priority with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(priorityBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getRenewalPriority("tenant-1", "contract-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/renewals/contract-1/priority");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the score breakdown on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(priorityBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getRenewalPriority("tenant-1", "contract-1");

    expect(result).toEqual({ ok: true, statusCode: 200, priority: priorityBody, error: null });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getRenewalPriority("tenant-1", "missing-contract");

    expect(result).toEqual({ ok: false, statusCode: 404, priority: null, error: "No contract found for id missing-contract." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getRenewalPriority("tenant-1", "contract-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.priority).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getCorrectionHistory (task E07/F03/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const historyBody = [
    {
      fieldName: "annualSpend",
      previousValue: "500000",
      newValue: "520000",
      correctedBy: "unattributed",
      correctedAt: "2026-09-06T08:00:00Z",
      reason: "Corrected from the signed order form.",
    },
  ];

  it("GETs <baseUrl>/api/contracts/{id}/corrections with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(historyBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getCorrectionHistory("tenant-1", "contract-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/contracts/contract-1/corrections");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the newest-first history on 200 (possibly empty)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(historyBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getCorrectionHistory("tenant-1", "contract-1");

    expect(result).toEqual({ ok: true, statusCode: 200, history: historyBody, error: null });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getCorrectionHistory("tenant-1", "missing-contract");

    expect(result).toEqual({ ok: false, statusCode: 404, history: null, error: "No contract found for id missing-contract." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getCorrectionHistory("tenant-1", "contract-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.history).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().correctContract (task E07/F03/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const correctionBody = {
    contractId: "contract-1",
    versionNumber: 2,
    correctedFields: ["annualSpend"],
    correctedAt: "2026-09-06T08:00:00Z",
  };

  it("PATCHes <baseUrl>/api/contracts/{id} with JSON corrections and the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(correctionBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").correctContract("tenant-1", "contract-1", {
      corrections: { annualSpend: "520000" },
      reason: "Corrected from the signed order form.",
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/contracts/contract-1");
    expect(init).toEqual({
      method: "PATCH",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify({ corrections: { annualSpend: "520000" }, reason: "Corrected from the signed order form." }),
      cache: "no-store",
    });
  });

  it("reports ok:true with the resulting version/correctedFields on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(correctionBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").correctContract("tenant-1", "contract-1", {
      corrections: { annualSpend: "520000" },
    });

    expect(result).toEqual({ ok: true, statusCode: 200, correction: correctionBody, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (a no-op correction, Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(JSON.stringify("None of the supplied values differ from the contract's current values."), { status: 400 }),
        ),
    );

    const result = await createApiClient("https://api.dev.contigo.example").correctContract("tenant-1", "contract-1", {
      corrections: { annualSpend: "500000" },
    });

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      correction: null,
      error: "None of the supplied values differ from the contract's current values.",
    });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").correctContract("tenant-1", "missing-contract", {
      corrections: { annualSpend: "520000" },
    });

    expect(result).toEqual({ ok: false, statusCode: 404, correction: null, error: "No contract found for id missing-contract." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").correctContract("tenant-1", "contract-1", {
      corrections: { annualSpend: "520000" },
    });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.correction).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().askContigo (task E07/F04/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const answeredBody = {
    question: "What liability do we have with AWS?",
    intent: "Semantic",
    canDetermine: true,
    answer: "AWS liability is capped at USD 500,000.",
    citations: [{ documentId: "Document:doc-1", page: null, section: "chunk 0" }],
    message: null,
  };

  it("POSTs <baseUrl>/api/chat/query with JSON {question} and the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(answeredBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").askContigo("tenant-1", {
      question: "What liability do we have with AWS?",
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/chat/query");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify({ question: "What liability do we have with AWS?" }),
      cache: "no-store",
    });
  });

  it("reports ok:true with the full routed envelope on 200 (a determined, cited answer)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(answeredBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").askContigo("tenant-1", {
      question: "What liability do we have with AWS?",
    });

    expect(result).toEqual({ ok: true, statusCode: 200, response: answeredBody, error: null });
  });

  it("reports ok:true on 200 even when canDetermine is false -- an honest abstain is not a client error", async () => {
    const abstainBody = {
      question: "What is our total liability exposure across all contracts?",
      intent: "Semantic",
      canDetermine: false,
      answer: null,
      citations: [],
      message: null,
    };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(abstainBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").askContigo("tenant-1", {
      question: "What is our total liability exposure across all contracts?",
    });

    expect(result).toEqual({ ok: true, statusCode: 200, response: abstainBody, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (a blank question, Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("A non-empty 'question' is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").askContigo("tenant-1", { question: "   " });

    expect(result).toEqual({ ok: false, statusCode: 400, response: null, error: "A non-empty 'question' is required." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").askContigo("tenant-1", {
      question: "What liability do we have with AWS?",
    });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.response).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().postRenewalAction (task E08/F01/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const actionBody = {
    contractId: "contract-1",
    owner: "user@example.test",
    status: "InProgress",
    action: "In negotiation",
    updatedAt: "2026-09-06T08:00:00Z",
  };

  it("POSTs JSON to <baseUrl>/api/renewals/{id}/action with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(actionBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").postRenewalAction("tenant-1", "contract-1", {
      owner: "user@example.test",
      status: "InProgress",
      action: "In negotiation",
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/renewals/contract-1/action");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify({ owner: "user@example.test", status: "InProgress", action: "In negotiation" }),
      cache: "no-store",
    });
  });

  it("reports ok:true with the upserted row on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(actionBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").postRenewalAction("tenant-1", "contract-1", {
      owner: "user@example.test",
      status: "InProgress",
      action: "In negotiation",
    });

    expect(result).toEqual({ ok: true, statusCode: 200, action: actionBody, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("'owner' is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").postRenewalAction("tenant-1", "contract-1", {
      owner: "",
      status: "InProgress",
      action: "In negotiation",
    } as never);

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      action: null,
      error: "'owner' is required.",
    });
  });

  it("falls back to a status-based message when a non-2xx body is not JSON", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Bad Gateway", { status: 502, statusText: "Bad Gateway" })));

    const result = await createApiClient("https://api.dev.contigo.example").postRenewalAction("tenant-1", "contract-1", {
      owner: "user@example.test",
      status: "InProgress",
      action: "In negotiation",
    });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBe(502);
    expect(result.action).toBeNull();
    expect(result.error).toContain("502");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").postRenewalAction("tenant-1", "contract-1", {
      owner: "user@example.test",
      status: "InProgress",
      action: "In negotiation",
    });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.action).toBeNull();
    expect(result.error).toContain("https://api.dev.contigo.example/api/renewals/contract-1/action");
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().uploadQuote (task E08/F03/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function quoteFile(name = "quote.pdf") {
    return new File(["%PDF-1.4"], name, { type: "application/pdf" });
  }

  const uploadedQuote = {
    id: "quote-1",
    fileName: "quote.pdf",
    mimeType: "application/pdf",
    processingStatus: "NeedsReview",
    lineItemCount: 3,
    normalizedLineItemCount: 2,
    unresolvedNormalizationCount: 1,
    unmatchedSkuCount: 1,
    supplier: "Databricks",
    currency: "CHF",
    geography: "CH",
    purchaseDate: "2026-09-05",
    createdAt: "2026-09-06T08:00:00Z",
  };

  it("POSTs multipart/form-data to <baseUrl>/api/quotes with the file and optional fields, plus the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(uploadedQuote), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").uploadQuote("tenant-1", quoteFile(), {
      supplier: "Databricks",
      currency: "CHF",
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/quotes");
    expect(init.method).toBe("POST");
    expect(init.headers).toEqual({ "X-Tenant-Id": "tenant-1" });
    expect(init.cache).toBe("no-store");
    const body = init.body as FormData;
    expect(body.get("file")).toBeInstanceOf(File);
    expect(body.get("supplier")).toBe("Databricks");
    expect(body.get("currency")).toBe("CHF");
    expect(body.get("geography")).toBeNull();
  });

  it("reports ok:true with the stored (and synchronously extracted) quote on 201", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(uploadedQuote), { status: 201 })));

    const result = await createApiClient("https://api.dev.contigo.example").uploadQuote("tenant-1", quoteFile());

    expect(result).toEqual({ ok: true, statusCode: 201, quote: uploadedQuote, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify("A non-empty 'file' form field is required."), { status: 400 })));

    const result = await createApiClient("https://api.dev.contigo.example").uploadQuote("tenant-1", quoteFile());

    expect(result).toEqual({ ok: false, statusCode: 400, quote: null, error: "A non-empty 'file' form field is required." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").uploadQuote("tenant-1", quoteFile());

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getQuoteAssessment (task E08/F03/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const assessmentBody = { quoteId: "quote-1", lines: [] };

  it("calls GET <baseUrl>/api/quotes/{id}/assessment with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(assessmentBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getQuoteAssessment("tenant-1", "quote-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/quotes/quote-1/assessment");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the assessment on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(assessmentBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getQuoteAssessment("tenant-1", "quote-1");

    expect(result).toEqual({ ok: true, statusCode: 200, assessment: assessmentBody, error: null });
  });

  it("reads the real 404 response body (Results.NotFound(result.Error)), unlike getContract360's own bare, empty-body 404", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify("Quote not found."), { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getQuoteAssessment("tenant-1", "missing-quote");

    expect(result).toEqual({ ok: false, statusCode: 404, assessment: null, error: "Quote not found." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getQuoteAssessment("tenant-1", "quote-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().recalculateQuoteAssessment (task E08/F03/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const recalculationBody = {
    quoteId: "quote-1",
    mappingsAppliedCount: 1,
    normalization: { lineCount: 3, matchedCount: 3, unmatchedCount: 0, notApplicableCount: 0 },
    unmatchedLines: [],
    assessment: { quoteId: "quote-1", lines: [] },
  };

  it("POSTs JSON {mappings} to <baseUrl>/api/quotes/{id}/assessment/recalculate, defaulting to an empty array", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(recalculationBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").recalculateQuoteAssessment("tenant-1", "quote-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/quotes/quote-1/assessment/recalculate");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify({ mappings: [] }),
      cache: "no-store",
    });
  });

  it("sends the supplied mappings verbatim", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(recalculationBody), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").recalculateQuoteAssessment("tenant-1", "quote-1", [
      { sku: "ENT-SUP-CUSTOM", canonicalSku: "ENT-SUP-STD" },
    ]);

    const [, init] = fetchMock.mock.calls[0];
    expect(init.body).toBe(JSON.stringify({ mappings: [{ sku: "ENT-SUP-CUSTOM", canonicalSku: "ENT-SUP-STD" }] }));
  });

  it("reports ok:true with the recalculation on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(recalculationBody), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").recalculateQuoteAssessment("tenant-1", "quote-1");

    expect(result).toEqual({ ok: true, statusCode: 200, recalculation: recalculationBody, error: null });
  });

  it("reads the real 404 response body (SkuMappingService.QuoteNotFoundError)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify("Quote not found."), { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").recalculateQuoteAssessment("tenant-1", "missing-quote");

    expect(result).toEqual({ ok: false, statusCode: 404, recalculation: null, error: "Quote not found." });
  });

  it("reports ok:false with the parsed JSON string error on 400 (a blank sku/canonicalSku)", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("'sku' is required for every manual product-mapping correction."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").recalculateQuoteAssessment("tenant-1", "quote-1", [
      { sku: "", canonicalSku: "x" },
    ]);

    expect(result.ok).toBe(false);
    expect(result.error).toContain("'sku' is required");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").recalculateQuoteAssessment("tenant-1", "quote-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().captureNegotiationOutcome (task E08/F03/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const outcomeBody = {
    id: "outcome-1",
    quoteId: "quote-1",
    originalQuoteTotal: 520_000,
    targetPrice: 420_000,
    finalPrice: 435_000,
    realizedSaving: 85_000,
    discountPercent: 16.35,
    negotiationDurationDays: 24,
    leversUsed: ["Term", "QuarterEnd"],
    capturedAt: "2026-09-06T08:00:00Z",
    savingsOpportunityId: null,
    savingsPropagated: null,
    savingsPropagationError: null,
  };

  const request = {
    quoteId: "quote-1",
    originalQuoteTotal: 520_000,
    targetPrice: 420_000,
    finalPrice: 435_000,
    negotiationDurationDays: 24,
    leversUsed: ["Term", "QuarterEnd"] as const,
  };

  it("POSTs JSON to <baseUrl>/api/negotiations/outcomes with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(outcomeBody), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").captureNegotiationOutcome("tenant-1", request);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/negotiations/outcomes");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify(request),
      cache: "no-store",
    });
  });

  it("reports ok:true with the server-computed outcome (realizedSaving/discountPercent) on 201", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(outcomeBody), { status: 201 })));

    const result = await createApiClient("https://api.dev.contigo.example").captureNegotiationOutcome("tenant-1", request);

    expect(result).toEqual({ ok: true, statusCode: 201, outcome: outcomeBody, error: null });
  });

  it("reads the real 404 response body (NegotiationOutcomeService.QuoteNotFoundError)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify("Quote not found."), { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").captureNegotiationOutcome("tenant-1", request);

    expect(result).toEqual({ ok: false, statusCode: 404, outcome: null, error: "Quote not found." });
  });

  it("reports ok:false with the parsed JSON string error on 400 (e.g. an invalid leversUsed entry)", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(
            JSON.stringify("'leversUsed' entries must each be one of: Volume, Term, Utilization, Alternatives, QuarterEnd, Bundle, PaymentTerms."),
            { status: 400 },
          ),
        ),
    );

    const result = await createApiClient("https://api.dev.contigo.example").captureNegotiationOutcome("tenant-1", request);

    expect(result.ok).toBe(false);
    expect(result.error).toContain("leversUsed");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").captureNegotiationOutcome("tenant-1", request);

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getSavingsKpis (task E08/F02/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const kpiSummary = {
    annualSpendAnalyzed: [{ currency: "CHF", amount: 6_270_000, contractCount: 9 }],
    contractsAnalyzedCount: 9,
    savingsIdentified: [{ currency: "CHF", low: 410_000, high: 590_000, count: 6, averageConfidence: 0.82 }],
    savingsInProgress: [{ currency: "CHF", low: 240_000, high: 240_000, count: 2, averageConfidence: 0.75 }],
    savingsRealized: [{ currency: "CHF", low: 85_000, high: 85_000, count: 1, averageConfidence: 0.91 }],
    upcomingRenewalsCount: 4,
  };

  it("GETs <baseUrl>/api/savings/kpis with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(kpiSummary), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getSavingsKpis("tenant-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/savings/kpis");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the KPI summary on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(kpiSummary), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getSavingsKpis("tenant-1");

    expect(result).toEqual({ ok: true, statusCode: 200, kpis: kpiSummary, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("A valid 'X-Tenant-Id' header (a GUID) is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").getSavingsKpis("tenant-1");

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      kpis: null,
      error: "A valid 'X-Tenant-Id' header (a GUID) is required.",
    });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getSavingsKpis("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.kpis).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getSavingsOpportunities (task E08/F02/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const opportunitiesPage = {
    items: [
      {
        id: "opp-1",
        supplierId: "supplier-1",
        contractId: "contract-1",
        type: "Renewal",
        currentSpend: 640_000,
        currency: "CHF",
        estimatedSavingsLow: 80_000,
        estimatedSavingsHigh: 120_000,
        confidence: 0.92,
        confidenceLevel: "High",
        status: "Identified",
        owner: null,
        createdAt: "2026-08-01T00:00:00Z",
        updatedAt: "2026-08-01T00:00:00Z",
        realizedAmount: null,
      },
    ],
    totalCount: 1,
  };

  it("GETs <baseUrl>/api/savings with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(opportunitiesPage), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getSavingsOpportunities("tenant-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/savings");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the opportunity list on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(opportunitiesPage), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getSavingsOpportunities("tenant-1");

    expect(result).toEqual({ ok: true, statusCode: 200, opportunities: opportunitiesPage, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("A valid 'X-Tenant-Id' header (a GUID) is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").getSavingsOpportunities("tenant-1");

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      opportunities: null,
      error: "A valid 'X-Tenant-Id' header (a GUID) is required.",
    });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getSavingsOpportunities("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.opportunities).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().inviteWorkspaceMember (task E06/F04/US01/T01)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("POSTs JSON to <baseUrl>/api/workspaces/{tenantId}/invites with email and role, without an X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: "m-1", email: "buyer@acme.example", role: "Procurement" }), { status: 201 }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").inviteWorkspaceMember("tenant-1", {
      email: "buyer@acme.example",
      role: "Procurement",
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/workspaces/tenant-1/invites");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: "buyer@acme.example", role: "Procurement" }),
      cache: "no-store",
    });
  });

  it("reports ok:true with the created membership on 201", async () => {
    const member = { id: "m-1", email: "buyer@acme.example", role: "Procurement" as const };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(member), { status: 201 })));

    const result = await createApiClient("https://api.dev.contigo.example").inviteWorkspaceMember("tenant-1", {
      email: "buyer@acme.example",
      role: "Procurement",
    });

    expect(result).toEqual({ ok: true, statusCode: 201, member, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("An 'email' is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").inviteWorkspaceMember("tenant-1", {
      email: "",
      role: "Procurement",
    });

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      member: null,
      error: "An 'email' is required.",
    });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").inviteWorkspaceMember("tenant-1", {
      email: "buyer@acme.example",
      role: "Procurement",
    });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.member).toBeNull();
    expect(result.error).toContain("https://api.dev.contigo.example/api/workspaces/tenant-1/invites");
    expect(result.error).toContain("network down");
  });
});

// Task E13/F09/US01/T04 (web-ask-v2, ADR-024 §6): conversations, the reply contract, the capability
// catalog, one market record, and the X-User-Id header every method above now sends when supplied --
// this task is the phase-4 writer of both the OpenAPI contract and this client for the six describes
// below (see this file's own header comment on X-User-Id's full provenance).

describe("createApiClient().listConversations (task E13/F09/US01/T04)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const conversations = [
    { id: "conv-1", title: "When does Salesforce expire?", scopeContractId: null, updatedAt: "2026-09-08T00:05:00Z" },
  ];

  it("GETs <baseUrl>/api/conversations with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(conversations), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").listConversations("tenant-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/conversations");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the caller's own conversations, most-recently-updated first, on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(conversations), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").listConversations("tenant-1");

    expect(result).toEqual({ ok: true, statusCode: 200, conversations, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400 (Results.BadRequest(string))", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("A valid 'X-Tenant-Id' header (a GUID) is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").listConversations("tenant-1");

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      conversations: null,
      error: "A valid 'X-Tenant-Id' header (a GUID) is required.",
    });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").listConversations("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.conversations).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().createConversation (task E13/F09/US01/T04)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const conversation = { id: "conv-1", title: "New chat", scopeContractId: null, updatedAt: "2026-09-08T00:00:00Z" };

  it("POSTs JSON {} to <baseUrl>/api/conversations with the X-Tenant-Id header when called with no request", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(conversation), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").createConversation("tenant-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/conversations");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify({}),
      cache: "no-store",
    });
  });

  it("sends scopeContractId when supplied (Contract 360 'Ask about it', /ask?scope=<contractId>)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ...conversation, scopeContractId: "contract-1" }), { status: 201 }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").createConversation("tenant-1", { scopeContractId: "contract-1" });

    const [, init] = fetchMock.mock.calls[0];
    expect(init.body).toBe(JSON.stringify({ scopeContractId: "contract-1" }));
  });

  it("reports ok:true with the created conversation on 201", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(conversation), { status: 201 })));

    const result = await createApiClient("https://api.dev.contigo.example").createConversation("tenant-1");

    expect(result).toEqual({ ok: true, statusCode: 201, conversation, error: null });
  });

  it("reports ok:false with the parsed JSON string error on 400", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("No contract found for scopeContractId."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").createConversation("tenant-1", { scopeContractId: "missing" });

    expect(result).toEqual({
      ok: false,
      statusCode: 400,
      conversation: null,
      error: "No contract found for scopeContractId.",
    });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").createConversation("tenant-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.conversation).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getConversation (task E13/F09/US01/T04)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const conversationDetail = {
    id: "conv-1",
    title: "When does Salesforce expire?",
    scopeContractId: null,
    createdAt: "2026-09-08T00:00:00Z",
    updatedAt: "2026-09-08T00:05:00Z",
    messages: [],
  };

  it("GETs <baseUrl>/api/conversations/{id} with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(conversationDetail), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getConversation("tenant-1", "conv-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/conversations/conv-1");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the conversation and its messages, oldest first, on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(conversationDetail), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getConversation("tenant-1", "conv-1");

    expect(result).toEqual({ ok: true, statusCode: 200, conversation: conversationDetail, error: null });
  });

  it("reports a named 404 (unknown id, another tenant's, or another user's -- one honest outcome) without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getConversation("tenant-1", "missing-conv");

    expect(result).toEqual({ ok: false, statusCode: 404, conversation: null, error: "No conversation found for id missing-conv." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getConversation("tenant-1", "conv-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.conversation).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().postMessage (task E13/F09/US01/T04)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const reply = {
    conversationId: "conv-1",
    messageId: "msg-1",
    kind: "answer",
    answerMarkdown: "Salesforce ends on **15 January 2027** [1].",
    citations: [],
    actions: [],
    provenance: { sources: ["tenant"], modelId: "fixture", promptVersion: "answer-v2.1", inputHash: "abc" },
    followUps: [],
  };

  it("POSTs JSON {question} to <baseUrl>/api/conversations/{id}/messages with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(reply), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").postMessage("tenant-1", "conv-1", { question: "When does Salesforce expire?" });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/conversations/conv-1/messages");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify({ question: "When does Salesforce expire?" }),
      cache: "no-store",
    });
  });

  it("reports ok:true with the routed reply on 200, even for an honest abstain/redirect/refusal (never a client error)", async () => {
    const abstainReply = { ...reply, kind: "abstain", answerMarkdown: "Nothing in the validated contracts supports a reliable answer." };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(abstainReply), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").postMessage("tenant-1", "conv-1", { question: "…" });

    expect(result).toEqual({ ok: true, statusCode: 200, reply: abstainReply, error: null });
  });

  it("reports a named 404 (unknown conversation) without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").postMessage("tenant-1", "missing-conv", { question: "…" });

    expect(result).toEqual({ ok: false, statusCode: 404, reply: null, error: "No conversation found for id missing-conv." });
  });

  it("reports ok:false with the parsed JSON string error on 400 (a blank question)", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify("A non-empty 'question' is required."), { status: 400 })),
    );

    const result = await createApiClient("https://api.dev.contigo.example").postMessage("tenant-1", "conv-1", { question: "   " });

    expect(result).toEqual({ ok: false, statusCode: 400, reply: null, error: "A non-empty 'question' is required." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").postMessage("tenant-1", "conv-1", { question: "…" });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.reply).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getCapabilities (task E13/F09/US01/T04)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const catalog = {
    version: "capabilities-v2.0",
    capabilities: [
      {
        key: "ask",
        title: "Ask Contigo",
        routePattern: "/ask",
        description: "Ask about dates, spend, notice periods and clauses.",
        exampleQuestions: ["What can Contigo do?", "When does this contract expire?"],
        roleGate: "any",
        availability: "always",
        howTo: [],
      },
    ],
  };

  it("GETs <baseUrl>/api/capabilities with no headers at all (tenant-agnostic, no signed-in user)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(catalog), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getCapabilities();

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/capabilities");
    expect(init).toEqual({ cache: "no-store" });
  });

  it("reports ok:true with the full, role-filtered catalog on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(catalog), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getCapabilities();

    expect(result).toEqual({ ok: true, statusCode: 200, catalog, error: null });
  });

  it("reports a status-based message on a non-2xx (CapabilitiesEndpointExtensions has no documented failure branch to parse)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Service Unavailable", { status: 503, statusText: "Service Unavailable" })));

    const result = await createApiClient("https://api.dev.contigo.example").getCapabilities();

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBe(503);
    expect(result.catalog).toBeNull();
    expect(result.error).toContain("503");
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getCapabilities();

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.catalog).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().getMarketRecord (task E13/F09/US01/T04)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const record = {
    recordId: "rec-1",
    title: "Salesforce · Sales Cloud Enterprise",
    category: "CRM",
    geography: "CH",
    band: { p25: 118, p50: 132, p75: 149, currency: "CHF" },
    provenance: "representative market data · mock feed · updated 2026-09-01",
    updatedAt: "2026-09-01T00:00:00Z",
  };

  it("GETs <baseUrl>/api/market/records/{id} with no X-Tenant-Id header (shared, read-only index, ADR-024)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(record), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getMarketRecord("rec-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/market/records/rec-1");
    expect(init).toEqual({ cache: "no-store" });
  });

  it("reports ok:true with the record on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(record), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getMarketRecord("rec-1");

    expect(result).toEqual({ ok: true, statusCode: 200, record, error: null });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getMarketRecord("missing-rec");

    expect(result).toEqual({ ok: false, statusCode: 404, record: null, error: "No market record found for id missing-rec." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getMarketRecord("rec-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.record).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient() X-User-Id header (task E13/F09/US01/T04, OQ-askv2-005/ADR-022)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("adds X-User-Id alongside X-Tenant-Id when getUserId resolves a real value", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify([]), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example", () => "buyer@acme.example").listConversations("tenant-1");

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers).toEqual({ "X-Tenant-Id": "tenant-1", "X-User-Id": "buyer@acme.example" });
  });

  it("adds X-User-Id even on a call that otherwise sends no headers at all (getHealth)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("Healthy", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example", () => "buyer@acme.example").getHealth();

    const [, init] = fetchMock.mock.calls[0];
    expect(init).toEqual({ headers: { "X-User-Id": "buyer@acme.example" }, cache: "no-store" });
  });

  it("omits the header key entirely (not an empty string) when no getUserId is supplied at all -- every pre-existing call site's exact-toEqual headers check keeps passing unchanged", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("Healthy", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getHealth();

    const [, init] = fetchMock.mock.calls[0];
    expect(init).toEqual({ cache: "no-store" });
  });

  it("omits the header key when getUserId resolves null (no signed-in account yet)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify([]), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example", () => null).listConversations("tenant-1");

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers).toEqual({ "X-Tenant-Id": "tenant-1" });
  });

  it("omits the header key when getUserId resolves a blank/whitespace-only string (never sends X-User-Id: '')", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify([]), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example", () => "   ").listConversations("tenant-1");

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers).toEqual({ "X-Tenant-Id": "tenant-1" });
  });
});

describe("createApiClient().getContractEvidence (review evidence pane, GET /api/contracts/{id}/evidence)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const evidence = [
    {
      fieldName: "annualSpend",
      value: "48000",
      confidence: 0.96,
      sourcePage: 1,
      sourceSpan: "EUR 48,000,",
      sourceDocumentId: "doc-1",
      sourceFileName: "msa.pdf",
      passage: "The annual subscription fee is EUR 48,000, invoiced yearly in advance.",
      highlightStart: 31,
      highlightLength: 11,
      modelId: "fixture-extract-model",
      extractedAt: "2026-09-09T10:00:00Z",
    },
  ];

  it("GETs <baseUrl>/api/contracts/{id}/evidence with the X-Tenant-Id header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(evidence), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").getContractEvidence("tenant-1", "contract-1");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/contracts/contract-1/evidence");
    expect(init).toEqual({ headers: { "X-Tenant-Id": "tenant-1" }, cache: "no-store" });
  });

  it("reports ok:true with the evidence rows on 200 (an empty array is still ok)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(evidence), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").getContractEvidence("tenant-1", "contract-1");

    expect(result).toEqual({ ok: true, statusCode: 200, evidence, error: null });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").getContractEvidence("tenant-1", "missing");

    expect(result).toEqual({ ok: false, statusCode: 404, evidence: null, error: "No contract found for id missing." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").getContractEvidence("tenant-1", "contract-1");

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});

describe("createApiClient().validateDocument (review sign-off, POST /api/documents/{id}/validate)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const validation = {
    documentId: "doc-1",
    contractId: "contract-1",
    processingStatus: "Completed",
    validatedAt: "2026-09-09T10:00:00Z",
    acceptedFields: ["currency", "autoRenewal"],
    alreadyValidated: false,
  };

  it("POSTs <baseUrl>/api/documents/{id}/validate with the accepted field names as a JSON body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(validation), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await createApiClient("https://api.dev.contigo.example").validateDocument("tenant-1", "doc-1", {
      acceptedFields: ["currency", "autoRenewal"],
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe("https://api.dev.contigo.example/api/documents/doc-1/validate");
    expect(init).toEqual({
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Tenant-Id": "tenant-1" },
      body: JSON.stringify({ acceptedFields: ["currency", "autoRenewal"] }),
      cache: "no-store",
    });
  });

  it("reports ok:true with the validation summary on 200", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(validation), { status: 200 })));

    const result = await createApiClient("https://api.dev.contigo.example").validateDocument("tenant-1", "doc-1", { acceptedFields: [] });

    expect(result).toEqual({ ok: true, statusCode: 200, validation, error: null });
  });

  it("surfaces a 409's own reason verbatim (the document is not reviewable yet)", async () => {
    const reason = "This document is still being processed; wait for extraction to finish before validating it.";
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(reason), { status: 409 })));

    const result = await createApiClient("https://api.dev.contigo.example").validateDocument("tenant-1", "doc-1", { acceptedFields: [] });

    expect(result).toEqual({ ok: false, statusCode: 409, validation: null, error: reason });
  });

  it("reports a named 404 without attempting to parse an empty body", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));

    const result = await createApiClient("https://api.dev.contigo.example").validateDocument("tenant-1", "missing-doc", { acceptedFields: [] });

    expect(result).toEqual({ ok: false, statusCode: 404, validation: null, error: "No document found for id missing-doc." });
  });

  it("resolves (does not throw) with statusCode null when the network request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    const result = await createApiClient("https://api.dev.contigo.example").validateDocument("tenant-1", "doc-1", { acceptedFields: [] });

    expect(result.ok).toBe(false);
    expect(result.statusCode).toBeNull();
    expect(result.error).toContain("network down");
  });
});
