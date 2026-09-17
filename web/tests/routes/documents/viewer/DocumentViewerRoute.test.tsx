import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useSearchParams } from "react-router-dom";
import type {
  ApiClient,
  Contract360Body,
  Contract360ClauseBody,
  ContractFieldEvidenceBody,
  GetDocumentPreviewResult,
  ReadBackDocument,
} from "../../../../src/api/client";
import DocumentViewerRoute from "../../../../src/routes/documents/viewer";
import {
  CITATION_UNRESOLVABLE_COPY,
  EMPTY_PAGE_COPY,
} from "../../../../src/routes/documents/viewer/documentViewerViewModel";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const DOCUMENT_ID = "55555555-5555-5555-5555-555555555555";
const CONTRACT_ID = "22222222-2222-2222-2222-222222222222";
const CLAUSE_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    listWorkspaces: vi.fn(),
    getWorkspaceMembers: vi.fn(),
    revokeInvitation: vi.fn(),
    removeMember: vi.fn(),
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
    acceptPendingInvitation: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    listDocuments: vi.fn(),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn(),
    getContractStrategy: vi.fn(),
    validateDocument: vi.fn(),
    postRenewalAction: vi.fn(),
    getQuote: vi.fn(),
    getNegotiationSteps: vi.fn(),
    putNegotiationSteps: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askRaffa: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function documentRow(overrides: Partial<ReadBackDocument> = {}): ReadBackDocument {
  return {
    id: DOCUMENT_ID,
    contractId: CONTRACT_ID,
    fileName: "Acme_MSA.pdf",
    mimeType: "application/pdf",
    documentType: "Msa",
    processingStatus: "Completed",
    createdAt: "2026-09-01T00:00:00Z",
    pageCount: 5,
    isPageCountLimited: false,
    ...overrides,
  };
}

function clause(overrides: Partial<Contract360ClauseBody> = {}): Contract360ClauseBody {
  return {
    clauseId: CLAUSE_ID,
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

function contract360(clauses: Contract360ClauseBody[] = [clause()]): Contract360Body {
  return {
    contractId: CONTRACT_ID,
    readiness: { state: "ready", stage: null, documentCount: 1, completedDocumentCount: 1 },
    header: {
      contractId: CONTRACT_ID,
      supplierId: null,
      supplierName: null,
      type: "Msa",
      status: "active",
      annualSpend: null,
      totalContractValue: null,
      startDate: null,
      endDate: null,
      renewalDate: null,
      cancellationDeadline: null,
      autoRenewal: false,
      risk: null,
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
        createdAt: "2026-09-01T00:00:00Z",
      },
      commercials: {
        annualSpend: null,
        totalContractValue: null,
        currency: "CHF",
        paymentTerms: null,
        autoRenewal: false,
        renewalTermMonths: null,
        lineItemCount: 0,
        lineItemAnnualCostTotal: null,
        lineItemTotalCostTotal: null,
      },
      products: [],
      clauses,
      obligations: [],
      risks: [],
      documents: [
        {
          documentId: DOCUMENT_ID,
          fileName: "Acme_MSA.pdf",
          mimeType: "application/pdf",
          documentType: "Msa",
          processingStatus: "Completed",
          createdAt: "2026-09-01T00:00:00Z",
        },
      ],
      benchmark: [],
      renewal: { endDate: null, renewalDate: null, cancellationDeadline: null, autoRenewal: false, renewalTermMonths: null },
      activity: [],
    },
  };
}

function previewOk(objectUrl: string): GetDocumentPreviewResult {
  return { ok: true, statusCode: 200, objectUrl, error: null };
}

// Task E23/F04/US01/T01 (NW-63r, AC-1): a field's evidence, widened with `box` (epic-23 feature-02).
function evidenceRow(overrides: Partial<ContractFieldEvidenceBody> = {}): ContractFieldEvidenceBody {
  return {
    fieldName: "startDate",
    value: "2026-01-01",
    confidence: 0.92,
    decision: "auto_accepted",
    sourcePage: 3,
    sourceSpan: "1 January 2026",
    box: { x: 100, y: 200, width: 300, height: 60 },
    sourceDocumentId: DOCUMENT_ID,
    sourceFileName: "Acme_MSA.pdf",
    passage: "...effective 1 January 2026...",
    highlightStart: 10,
    highlightLength: 15,
    modelId: "gpt-x",
    extractedAt: "2026-09-01T00:00:00Z",
    ...overrides,
  };
}

/** jsdom never actually decodes an <img>'s bytes, so `naturalWidth`/`naturalHeight` stay 0 and
 * `onLoad` never fires on its own -- this stands in for "the page image finished loading" the same
 * way a real browser would report it. */
function fireImageLoad(image: HTMLElement, width: number, height: number) {
  Object.defineProperty(image, "naturalWidth", { value: width, configurable: true });
  Object.defineProperty(image, "naturalHeight", { value: height, configurable: true });
  fireEvent.load(image);
}

function readyClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return mockApiClient({
    getDocument: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, document: documentRow(), error: null }),
    getDocumentPreviewUrl: vi.fn().mockResolvedValue(previewOk("blob:page")),
    getContract360: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, contract: contract360(), error: null }),
    ...overrides,
  });
}

function SearchProbe() {
  const [params] = useSearchParams();
  return <div data-testid="viewer-search">{params.toString()}</div>;
}

function renderViewer(apiClient: ApiClient, path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <SearchProbe />
      <Routes>
        <Route path="/documents/:documentId/viewer" element={<DocumentViewerRoute apiClient={apiClient} />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("DocumentViewerRoute (task E22/F03/US01/T01)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("renders a skeleton in the page canvas while the document loads", () => {
    const apiClient = readyClient({
      getDocument: vi.fn().mockReturnValue(new Promise(() => undefined)),
    });
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=2`);
    expect(document.querySelector(".skeleton")).not.toBeNull();
    expect(apiClient.getDocumentPreviewUrl).not.toHaveBeenCalled();
  });

  it("?page= drives which page is fetched and paints Page N of M", async () => {
    const apiClient = readyClient();
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=2`);
    await waitFor(() => expect(apiClient.getDocumentPreviewUrl).toHaveBeenCalled());
    expect(apiClient.getDocumentPreviewUrl).toHaveBeenCalledWith(WORKSPACE_ID, DOCUMENT_ID, 2);
    expect(screen.getByText("Page 2 of 5")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Document page" })).toHaveAttribute("src", "blob:page");
  });

  it("page > pageCount renders not-found without calling the preview client, and keeps the URL", async () => {
    const apiClient = readyClient();
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=9`);
    await waitFor(() => expect(screen.getByText("This document has 5 pages.")).toBeInTheDocument());
    expect(apiClient.getDocumentPreviewUrl).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Go to page 1" })).toBeInTheDocument();
    expect(screen.getByTestId("viewer-search")).toHaveTextContent("page=9");
    expect(screen.getByText("Page 9 of 5")).toBeInTheDocument();
  });

  it("empty is 'no page image yet' when a counted-unknown page 404s", async () => {
    const apiClient = readyClient({
      getDocument: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        document: documentRow({ pageCount: null }),
        error: null,
      }),
      getDocumentPreviewUrl: vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 404,
        objectUrl: null,
        error: "No preview for page 1 of document x.",
      }),
    });
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=1`);
    await waitFor(() => expect(screen.getByText(EMPTY_PAGE_COPY)).toBeInTheDocument());
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
  });

  it("a server 404 for a counted page renders the reaped sentence, not Retry", async () => {
    const apiClient = readyClient({
      getDocumentPreviewUrl: vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 404,
        objectUrl: null,
        error: "No preview for page 2 of document x.",
      }),
    });
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=2`);
    await waitFor(() =>
      expect(
        screen.getByText("This page is no longer part of this document — it was re-processed after this link was made."),
      ).toBeInTheDocument(),
    );
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reload the document" })).toBeInTheDocument();
    expect(screen.queryByText(/document was not found/i)).not.toBeInTheDocument();
  });

  it("error names the service and offers Retry", async () => {
    const apiClient = readyClient({
      getDocument: vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 503,
        document: null,
        error: "unavailable",
      }),
    });
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=1`);
    await waitFor(() => expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument());
    expect(screen.getByText(/Raffa\.ai's document preview service/)).toBeInTheDocument();
  });

  it("revokes the object URL on unmount and on page change", async () => {
    const revoke = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
    const apiClient = readyClient({
      getDocumentPreviewUrl: vi
        .fn()
        .mockResolvedValueOnce(previewOk("blob:page-1"))
        .mockResolvedValueOnce(previewOk("blob:page-2")),
    });
    const user = userEvent.setup();
    const { unmount } = renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=1`);
    await waitFor(() => expect(screen.getByRole("img")).toHaveAttribute("src", "blob:page-1"));

    await user.click(screen.getByRole("button", { name: "Next" }));
    await waitFor(() => expect(apiClient.getDocumentPreviewUrl).toHaveBeenCalledWith(WORKSPACE_ID, DOCUMENT_ID, 2));
    expect(revoke).toHaveBeenCalledWith("blob:page-1");

    unmount();
    expect(revoke).toHaveBeenCalledWith("blob:page-2");
  });

  it("an unresolvable citation renders page 1 + the notice, no highlight, no Retry, never 'document not found'", async () => {
    const apiClient = readyClient();
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=3&clause=missing-clause`);
    await waitFor(() => expect(screen.getByTestId("document-viewer-citation-notice")).toBeInTheDocument());
    const notice = screen.getByTestId("document-viewer-citation-notice");
    expect(within(notice).getByText(CITATION_UNRESOLVABLE_COPY)).toBeInTheDocument();
    expect(within(notice).queryByText(/Retry/)).not.toBeInTheDocument();
    expect(within(notice).queryByText(/document.*not found/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/highlighted below/)).not.toBeInTheDocument();
    expect(screen.queryByTestId("clause-highlight")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
    await waitFor(() => expect(apiClient.getDocumentPreviewUrl).toHaveBeenCalledWith(WORKSPACE_ID, DOCUMENT_ID, 1));
    expect(screen.getByText("Page 1 of 5")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next" })).not.toBeDisabled();
  });

  it("a resolved clause shows the wording highlighted below and not a drawn shape on the page", async () => {
    const apiClient = readyClient();
    renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=3&clause=${CLAUSE_ID}`);
    await waitFor(() => expect(screen.getByTestId("clause-highlight")).toBeInTheDocument());
    expect(screen.getByText("Page 3 — the wording is highlighted below")).toBeInTheDocument();
    expect(screen.queryByTestId("document-viewer-citation-notice")).not.toBeInTheDocument();
    expect(apiClient.getDocumentPreviewUrl).toHaveBeenCalledWith(WORKSPACE_ID, DOCUMENT_ID, 3);
    // Unmocked getContractEvidence (mockApiClient's bare vi.fn()) resolves to `undefined`, which the
    // route must read as "no boxes" rather than throw -- the exact w17 fallback this test's own name
    // asserts (task E23/F04/US01/T01 widens this route without breaking it).
    expect(screen.queryByTestId("document-viewer-box")).not.toBeInTheDocument();
  });

  // Task E23/F04/US01/T01 (NW-63r, epic-23 feature-04, AC-1): the bounding-box overlay.
  describe("bounding-box overlay (task E23/F04/US01/T01)", () => {
    it("a cited field's box on the current page is drawn over the <img>, alongside the text highlight", async () => {
      const apiClient = readyClient({
        getContractEvidence: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          evidence: [evidenceRow({ fieldName: "startDate", sourcePage: 3 })],
          autoAcceptThreshold: 0.9,
          error: null,
        }),
      });
      renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=3&clause=${CLAUSE_ID}`);

      await waitFor(() => expect(apiClient.getContractEvidence).toHaveBeenCalledWith(WORKSPACE_ID, CONTRACT_ID));
      const image = await screen.findByRole("img", { name: "Document page" });
      fireImageLoad(image, 1000, 2000);

      const box = await screen.findByTestId("document-viewer-box");
      expect(box).toHaveStyle({ left: "10%", top: "10%", width: "30%", height: "3%" });
      // The box is additive (ADR-029 w18 footer clause 3: "draws a box on the page and shows the
      // text") -- it must not replace the existing text-level highlight.
      expect(screen.getByTestId("clause-highlight")).toBeInTheDocument();
    });

    it("a field's evidence on another page never bleeds its box onto this one", async () => {
      const apiClient = readyClient({
        getContractEvidence: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          evidence: [evidenceRow({ fieldName: "startDate", sourcePage: 4 })],
          autoAcceptThreshold: 0.9,
          error: null,
        }),
      });
      renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=3`);

      const image = await screen.findByRole("img", { name: "Document page" });
      fireImageLoad(image, 1000, 2000);

      expect(screen.queryByTestId("document-viewer-box")).not.toBeInTheDocument();
    });

    it("a null box degrades to the existing text-level highlight only (epic-23 AC-4)", async () => {
      const apiClient = readyClient({
        getContractEvidence: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          evidence: [evidenceRow({ fieldName: "startDate", sourcePage: 3, box: null })],
          autoAcceptThreshold: 0.9,
          error: null,
        }),
      });
      renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=3&clause=${CLAUSE_ID}`);

      const image = await screen.findByRole("img", { name: "Document page" });
      fireImageLoad(image, 1000, 2000);
      await waitFor(() => expect(screen.getByTestId("clause-highlight")).toBeInTheDocument());

      expect(screen.queryByTestId("document-viewer-box")).not.toBeInTheDocument();
    });

    it("a getContractEvidence failure degrades to no boxes, never a viewer-wide error", async () => {
      const apiClient = readyClient({
        getContractEvidence: vi.fn().mockResolvedValue({
          ok: false,
          statusCode: 503,
          evidence: null,
          error: "unavailable",
        }),
      });
      renderViewer(apiClient, `/documents/${DOCUMENT_ID}/viewer?page=3&clause=${CLAUSE_ID}`);

      await waitFor(() => expect(screen.getByTestId("clause-highlight")).toBeInTheDocument());
      expect(screen.queryByTestId("document-viewer-box")).not.toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
  });
});
