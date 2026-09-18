import { describe, expect, it } from "vitest";
import type { Contract360ClauseBody } from "../../../../src/api/client";
import {
  CITATION_UNRESOLVABLE_COPY,
  FIRST_PAGE,
  PAGE_NAV_TOTAL_UNKNOWN,
  buildDocumentViewerHref,
  formatBeyondCountCopy,
  formatHighlightAffordance,
  formatPageNav,
  parseDocumentViewerHref,
  parsePositivePage,
  previewNotFoundCause,
  resolveCitation,
  resolveViewerSurface,
} from "../../../../src/routes/documents/viewer/documentViewerViewModel";

const DOCUMENT_ID = "doc-1";

function clause(overrides: Partial<Contract360ClauseBody> = {}): Contract360ClauseBody {
  return {
    clauseId: "clause-1",
    clauseType: "Termination",
    rawText: "Either party may terminate on ninety days' written notice.",
    normalizedValue: "90 days' notice",
    riskLevel: "Medium",
    sourceDocumentId: DOCUMENT_ID,
    sourceSpan: "§8.1",
    sourcePage: 3,
    confidence: 0.91,
    ...overrides,
  };
}

describe("parsePositivePage", () => {
  it("accepts a 1-based positive integer", () => {
    expect(parsePositivePage("1")).toBe(1);
    expect(parsePositivePage("12")).toBe(12);
  });

  it("rejects a missing, empty, zero, negative, or non-integer page", () => {
    expect(parsePositivePage(null)).toBeNull();
    expect(parsePositivePage("")).toBeNull();
    expect(parsePositivePage("0")).toBeNull();
    expect(parsePositivePage("-1")).toBeNull();
    expect(parsePositivePage("3.2")).toBeNull();
    expect(parsePositivePage("foo")).toBeNull();
  });
});

describe("formatPageNav / beyond-count copy", () => {
  it("yields Page N of M when pageCount is known", () => {
    expect(formatPageNav(2, 5)).toBe("Page 2 of 5");
  });

  it("a null pageCount yields Page 1 · total unknown", () => {
    expect(formatPageNav(FIRST_PAGE, null)).toBe(PAGE_NAV_TOTAL_UNKNOWN);
    expect(formatPageNav(3, null)).toBe(PAGE_NAV_TOTAL_UNKNOWN);
  });

  it("page > pageCount yields the first not-found cause", () => {
    expect(formatBeyondCountCopy(5)).toBe("This document has 5 pages.");
    expect(resolveViewerSurface({
      documentId: DOCUMENT_ID,
      pageParam: "9",
      clauseParam: null,
      pageCount: 5,
      clauses: null,
    })).toEqual({ kind: "beyond-count", pageCount: 5, askedPage: 9 });
  });

  it("a server 404 for a counted page yields the reaped cause; a null count yields empty", () => {
    expect(previewNotFoundCause(5)).toBe("reaped");
    expect(previewNotFoundCause(null)).toBe("empty");
  });
});

describe("resolveCitation (step 12 triggers)", () => {
  it("arrived-cold (no query) is unresolvable", () => {
    expect(resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: null,
      clauseParam: null,
      clauses: [clause()],
    })).toEqual({ kind: "unresolvable" });
  });

  it("a page that is not a positive integer is unresolvable", () => {
    expect(resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: "foo",
      clauseParam: "clause-1",
      clauses: [clause()],
    }).kind).toBe("unresolvable");
    expect(resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: "0",
      clauseParam: null,
      clauses: [clause()],
    }).kind).toBe("unresolvable");
  });

  it("an unknown clause is unresolvable", () => {
    expect(resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: "3",
      clauseParam: "missing",
      clauses: [clause()],
    }).kind).toBe("unresolvable");
  });

  it("a clause that is not in this document is unresolvable", () => {
    expect(resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: "3",
      clauseParam: "clause-1",
      clauses: [clause({ sourceDocumentId: "other-doc" })],
    }).kind).toBe("unresolvable");
  });

  it("a clause with no resolvable sourcePage is unresolvable", () => {
    expect(resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: "3",
      clauseParam: "clause-1",
      clauses: [clause({ sourcePage: null })],
    }).kind).toBe("unresolvable");
  });

  it("a valid ?page= with no clause is paging, not a citation", () => {
    expect(resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: "2",
      clauseParam: null,
      clauses: [clause()],
    })).toEqual({ kind: "none" });
  });

  it("a resolved clause yields the highlight affordance on its sourcePage when ?page= is absent", () => {
    const resolved = resolveCitation({
      documentId: DOCUMENT_ID,
      pageParam: null,
      clauseParam: "clause-1",
      clauses: [clause()],
    });
    expect(resolved).toEqual({ kind: "resolved", clause: clause(), sourcePage: 3 });
    const surface = resolveViewerSurface({
      documentId: DOCUMENT_ID,
      pageParam: null,
      clauseParam: "clause-1",
      pageCount: 12,
      clauses: [clause()],
    });
    expect(surface).toEqual({
      kind: "page",
      page: 3,
      fetchPage: 3,
      citation: resolved,
    });
    expect(formatHighlightAffordance(3)).toBe("Page 3 — the wording is highlighted below");
  });

  it("an unresolvable citation is an ordinary page on the first page, not a not-found", () => {
    const surface = resolveViewerSurface({
      documentId: DOCUMENT_ID,
      pageParam: "3",
      clauseParam: "missing",
      pageCount: 12,
      clauses: [clause()],
    });
    expect(surface).toEqual({
      kind: "page",
      page: FIRST_PAGE,
      fetchPage: FIRST_PAGE,
      citation: { kind: "unresolvable" },
    });
    expect(CITATION_UNRESOLVABLE_COPY).toBe("The citation could not be restored.");
  });

  it("a null pageCount shows the first page and the unknown-total string", () => {
    const surface = resolveViewerSurface({
      documentId: DOCUMENT_ID,
      pageParam: "2",
      clauseParam: null,
      pageCount: null,
      clauses: null,
    });
    expect(surface).toEqual({
      kind: "page",
      page: FIRST_PAGE,
      fetchPage: FIRST_PAGE,
      citation: { kind: "none" },
    });
    expect(formatPageNav(surface.kind === "page" ? surface.page : FIRST_PAGE, null)).toBe(PAGE_NAV_TOTAL_UNKNOWN);
  });
});

describe("parseDocumentViewerHref / buildDocumentViewerHref", () => {
  it("parses document id, page and clause from the viewer route", () => {
    expect(parseDocumentViewerHref("/documents/doc-1/viewer?page=12&clause=clause-1")).toEqual({
      documentId: "doc-1",
      page: "12",
      clause: "clause-1",
    });
  });

  it("returns null for a non-viewer href", () => {
    expect(parseDocumentViewerHref("/contracts/contract-1")).toBeNull();
    expect(parseDocumentViewerHref("/documents")).toBeNull();
  });

  it("round-trips through buildDocumentViewerHref", () => {
    expect(buildDocumentViewerHref("doc-1", 2, "cl-1")).toBe("/documents/doc-1/viewer?page=2&clause=cl-1");
    expect(buildDocumentViewerHref("doc-1")).toBe("/documents/doc-1/viewer");
  });
});
