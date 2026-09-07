import { describe, expect, it, vi } from "vitest";
import type { ApiClient, GetDocumentResult } from "../../../src/api/client";
import {
  ROUTE_LINE_BY_INTENT,
  SEMANTIC_ABSTAIN_REASON,
  buildCitationViews,
  buildContigoMessage,
  buildErrorMessage,
  buildYouMessage,
  nextMessageId,
  parseCitationSource,
  resolveCitationContractId,
} from "../../../src/routes/ask/askViewModel";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    // Task E08/F01/US01/T01 (renewal-pipeline) / E08/F03/US01/T01 (quote-check-ui): this suite never
    // exercises anything beyond the pure ask view-model helpers -- bare vi.fn() is enough, same
    // convention as getRenewalPriority above. (Pre-existing gap in this file's own mock literal,
    // backfilled here while task E08/F02/US01/T01 was already touching this exact object for its own
    // two additions below.)
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    // Task E08/F02/US01/T01 (savings-home): same reasoning as above.
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function documentOk(overrides: Partial<NonNullable<GetDocumentResult["document"]>> = {}): GetDocumentResult {
  return {
    ok: true,
    statusCode: 200,
    document: {
      id: "doc-1",
      contractId: "contract-1",
      fileName: "Acme_MSA.pdf",
      mimeType: "application/pdf",
      documentType: "Msa",
      processingStatus: "Completed",
      createdAt: "2025-01-01T00:00:00Z",
      ...overrides,
    },
    error: null,
  };
}

describe("parseCitationSource", () => {
  it("splits a composite SourceType:SourceId id into its two halves", () => {
    expect(parseCitationSource("Document:3fa85f64-5717-4562-b3fc-2c963f66afa6")).toEqual({
      sourceType: "Document",
      sourceId: "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    });
    expect(parseCitationSource("Clause:cl-1")).toEqual({ sourceType: "Clause", sourceId: "cl-1" });
  });

  it("returns null for a string with no colon, a leading colon, or a trailing colon", () => {
    expect(parseCitationSource("no-colon-here")).toBeNull();
    expect(parseCitationSource(":missing-type")).toBeNull();
    expect(parseCitationSource("missing-id:")).toBeNull();
  });
});

describe("buildCitationViews", () => {
  it("numbers citations 1-based within this call, in the given order", () => {
    const views = buildCitationViews([
      { documentId: "Document:doc-1", page: null, section: "chunk 0" },
      { documentId: "Clause:cl-1", page: 12, section: "§4.1" },
    ]);

    expect(views.map((v) => v.n)).toEqual([1, 2]);
    expect(views[0]).toMatchObject({ sourceType: "Document", sourceId: "doc-1", page: null, section: "chunk 0" });
    expect(views[1]).toMatchObject({ sourceType: "Clause", sourceId: "cl-1", page: 12, section: "§4.1" });
  });

  it("leaves sourceType/sourceId null for an unparsable documentId instead of guessing", () => {
    const views = buildCitationViews([{ documentId: "not-a-composite-id", page: null, section: null }]);

    expect(views[0].sourceType).toBeNull();
    expect(views[0].sourceId).toBeNull();
    expect(views[0].documentId).toBe("not-a-composite-id");
  });
});

describe("buildYouMessage", () => {
  it("builds a plain 'you' turn with no route, kind, reason, or citations", () => {
    expect(buildYouMessage("m1", "What is our AWS spend?")).toEqual({
      id: "m1",
      role: "you",
      text: "What is our AWS spend?",
      route: null,
      kind: "answer",
      reason: null,
      citations: [],
    });
  });
});

describe("buildContigoMessage", () => {
  it("AC-2: a determined Semantic answer carries the answer text, numbered citations, and the 'Clause retrieval…' route line", () => {
    const message = buildContigoMessage("m2", {
      question: "What liability do we have with AWS?",
      intent: "Semantic",
      canDetermine: true,
      answer: "AWS liability is capped at USD 500,000.",
      citations: [{ documentId: "Document:doc-1", page: null, section: "chunk 0" }],
      message: null,
    });

    expect(message.kind).toBe("answer");
    expect(message.text).toBe("AWS liability is capped at USD 500,000.");
    expect(message.route).toBe(ROUTE_LINE_BY_INTENT.Semantic);
    expect(message.route).toBe("Clause retrieval…");
    expect(message.citations).toHaveLength(1);
    expect(message.reason).toBeNull();
  });

  it("AC-3: a Semantic canDetermine:false turn abstains with the fixed, honest 'no evidence' reason (message is always null on this branch)", () => {
    const message = buildContigoMessage("m3", {
      question: "What is our total liability exposure across all contracts?",
      intent: "Semantic",
      canDetermine: false,
      answer: null,
      citations: [],
      message: null,
    });

    expect(message.kind).toBe("abstain");
    expect(message.text).toBe("");
    expect(message.reason).toBe(SEMANTIC_ABSTAIN_REASON);
    expect(message.route).toBe("Clause retrieval…");
    expect(message.citations).toEqual([]);
  });

  it("AC-4 unknown-question fallback: a Structured, not-yet-wired turn abstains with the backend's own message and the 'Structured query…' route line", () => {
    const message = buildContigoMessage("m4", {
      question: "Which contracts renew in the next 45 days?",
      intent: "Structured",
      canDetermine: false,
      answer: null,
      citations: [],
      message: "This looks like a structured/deterministic question ... ask a semantic/legal question instead.",
    });

    expect(message.kind).toBe("abstain");
    expect(message.reason).toBe("This looks like a structured/deterministic question ... ask a semantic/legal question instead.");
    expect(message.route).toBe("Structured query…");
  });

  it("treats canDetermine:true with a null answer as an abstain rather than crashing on the missing text", () => {
    const message = buildContigoMessage("m5", {
      question: "…",
      intent: "Semantic",
      canDetermine: true,
      answer: null,
      citations: [],
      message: null,
    });

    expect(message.kind).toBe("abstain");
  });
});

describe("buildErrorMessage", () => {
  it("is a distinct kind from an honest AI abstention", () => {
    const message = buildErrorMessage("m6", "Contigo's Q&A service is temporarily unavailable. Try again in a moment.");

    expect(message.kind).toBe("error");
    expect(message.kind).not.toBe("abstain");
    expect(message.reason).toBe("Contigo's Q&A service is temporarily unavailable. Try again in a moment.");
  });
});

describe("nextMessageId", () => {
  it("never repeats within one process", () => {
    const first = nextMessageId();
    const second = nextMessageId();

    expect(first).not.toBe(second);
  });
});

describe("resolveCitationContractId (AC-2 'opening Contract 360 > Clauses')", () => {
  it("resolves a Document-sourced citation to its contractId via getDocument", async () => {
    const getDocument = vi.fn().mockResolvedValue(documentOk());
    const apiClient = mockApiClient({ getDocument });

    const result = await resolveCitationContractId(apiClient, "tenant-1", {
      n: 1,
      documentId: "Document:doc-1",
      sourceType: "Document",
      sourceId: "doc-1",
      page: null,
      section: "chunk 0",
    });

    expect(getDocument).toHaveBeenCalledWith("tenant-1", "doc-1");
    expect(result).toEqual({ ok: true, contractId: "contract-1" });
  });

  it("names the gap for a Clause-sourced citation instead of guessing a contract id", async () => {
    const getDocument = vi.fn();
    const apiClient = mockApiClient({ getDocument });

    const result = await resolveCitationContractId(apiClient, "tenant-1", {
      n: 1,
      documentId: "Clause:cl-1",
      sourceType: "Clause",
      sourceId: "cl-1",
      page: 12,
      section: "§4.1",
    });

    expect(getDocument).not.toHaveBeenCalled();
    expect(result.ok).toBe(false);
    expect(!result.ok && result.reason).toMatch(/clause-level citation/i);
  });

  it("names the gap for a citation whose documentId did not parse at all", async () => {
    const apiClient = mockApiClient();

    const result = await resolveCitationContractId(apiClient, "tenant-1", {
      n: 1,
      documentId: "not-a-composite-id",
      sourceType: null,
      sourceId: null,
      page: null,
      section: null,
    });

    expect(result.ok).toBe(false);
  });

  it("reports an honest 'not linked yet' reason when the document exists but has no contractId", async () => {
    const apiClient = mockApiClient({ getDocument: vi.fn().mockResolvedValue(documentOk({ contractId: null })) });

    const result = await resolveCitationContractId(apiClient, "tenant-1", {
      n: 1,
      documentId: "Document:doc-1",
      sourceType: "Document",
      sourceId: "doc-1",
      page: null,
      section: "chunk 0",
    });

    expect(result).toEqual({ ok: false, reason: "This document is not linked to a contract yet." });
  });

  it("surfaces getDocument's own error when the lookup fails (e.g. a 404)", async () => {
    const apiClient = mockApiClient({
      getDocument: vi.fn().mockResolvedValue({ ok: false, statusCode: 404, document: null, error: "No document found for id doc-1." }),
    });

    const result = await resolveCitationContractId(apiClient, "tenant-1", {
      n: 1,
      documentId: "Document:doc-1",
      sourceType: "Document",
      sourceId: "doc-1",
      page: null,
      section: "chunk 0",
    });

    expect(result).toEqual({ ok: false, reason: "No document found for id doc-1." });
  });
});
