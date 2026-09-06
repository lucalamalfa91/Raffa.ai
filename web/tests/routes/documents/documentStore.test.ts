import { beforeEach, describe, expect, it } from "vitest";
import { loadTrackedDocuments, rememberDocument, type TrackedDocument } from "../../../src/routes/documents/documentStore";

function trackedDocument(overrides: Partial<TrackedDocument> = {}): TrackedDocument {
  return {
    id: "doc-1",
    contractId: "contract-1",
    fileName: "Acme_MSA.pdf",
    documentType: null,
    processingStatus: "Completed",
    createdAt: "2026-09-06T08:00:00Z",
    ...overrides,
  };
}

describe("documentStore (us-02-document-status-readback: session-scoped document table)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("returns an empty list before anything has been remembered", () => {
    expect(loadTrackedDocuments()).toEqual([]);
  });

  it("persists a remembered document across loadTrackedDocuments calls", () => {
    rememberDocument(trackedDocument());

    expect(loadTrackedDocuments()).toEqual([trackedDocument()]);
  });

  it("prepends new documents (most-recently-touched first)", () => {
    rememberDocument(trackedDocument({ id: "doc-1", fileName: "First.pdf" }));
    rememberDocument(trackedDocument({ id: "doc-2", fileName: "Second.pdf" }));

    const loaded = loadTrackedDocuments();
    expect(loaded.map((document) => document.fileName)).toEqual(["Second.pdf", "First.pdf"]);
  });

  it("updates (does not duplicate) an existing document by id, moving it to the front", () => {
    rememberDocument(trackedDocument({ id: "doc-1", documentType: null }));
    rememberDocument(trackedDocument({ id: "doc-2", fileName: "Other.pdf" }));

    rememberDocument(trackedDocument({ id: "doc-1", documentType: "Msa" }));

    const loaded = loadTrackedDocuments();
    expect(loaded).toHaveLength(2);
    expect(loaded[0]).toEqual(trackedDocument({ id: "doc-1", documentType: "Msa" }));
    expect(loaded[1].id).toBe("doc-2");
  });

  it("treats malformed sessionStorage content as an empty list rather than throwing", () => {
    window.sessionStorage.setItem("contigo.documents.readback", "not json");

    expect(loadTrackedDocuments()).toEqual([]);
  });

  it("treats a non-array JSON payload under the key as an empty list", () => {
    window.sessionStorage.setItem("contigo.documents.readback", JSON.stringify({ not: "an array" }));

    expect(loadTrackedDocuments()).toEqual([]);
  });

  it("accepts an explicit Storage instance instead of the window.sessionStorage default", () => {
    const customStorage = window.localStorage; // any Storage-shaped object works
    customStorage.clear();

    rememberDocument(trackedDocument(), customStorage);

    expect(loadTrackedDocuments(customStorage)).toEqual([trackedDocument()]);
    expect(loadTrackedDocuments()).toEqual([]); // default sessionStorage untouched
    customStorage.clear();
  });
});
