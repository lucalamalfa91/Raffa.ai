import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import DocumentsRoute from "../../../src/routes/documents";
import type {
  ApiClient,
  Contract360Body,
  CorrectionHistoryEntryBody,
  DocumentListItemBody,
  DocumentListPageBody,
  GetContract360Result,
  GetCorrectionHistoryResult,
  ListDocumentsResult,
  UploadDocumentResult,
} from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getHealth: vi.fn(),
    createWorkspace: vi.fn(),
    inviteWorkspaceMember: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    // Task E13/F09/US01/T04 (web-ask-v2): this suite never reaches conversations/capabilities/
    // market -- bare vi.fn() is enough, same convention as getContract360 below.
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    listDocuments: vi.fn().mockResolvedValue(emptyPage()),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    postRenewalAction: vi.fn(),
    uploadQuote: vi.fn(),
    getQuoteAssessment: vi.fn(),
    recalculateQuoteAssessment: vi.fn(),
    captureNegotiationOutcome: vi.fn(),
    askContigo: vi.fn(),
    getSavingsKpis: vi.fn(),
    getSavingsOpportunities: vi.fn(),
    ...overrides,
  };
}

function docItem(overrides: Partial<DocumentListItemBody> = {}): DocumentListItemBody {
  return {
    id: "doc-1",
    contractId: "contract-1",
    supplierName: "Salesforce",
    fileName: "Salesforce_MSA.pdf",
    documentType: "Msa",
    processingStatus: "Completed",
    stage: null,
    pageCount: 12,
    createdAt: "2026-09-06T08:05:00Z",
    weakFactCount: 0,
    ...overrides,
  };
}

function page(items: DocumentListItemBody[]): DocumentListPageBody {
  return { items, page: 1, pageSize: 100, totalCount: items.length };
}

function emptyPage(): ListDocumentsResult {
  return { ok: true, statusCode: 200, page: page([]), error: null };
}

function listOk(items: DocumentListItemBody[]): ListDocumentsResult {
  return { ok: true, statusCode: 200, page: page(items), error: null };
}

function uploadedOk(overrides: Partial<NonNullable<UploadDocumentResult["document"]>> = {}): UploadDocumentResult {
  return {
    ok: true,
    statusCode: 201,
    document: {
      id: "doc-new",
      contractId: "contract-new",
      fileName: "New.pdf",
      mimeType: "application/pdf",
      processingStatus: "Completed",
      createdAt: "2026-09-06T09:00:00Z",
      ...overrides,
    },
    rejection: null,
    error: null,
  };
}

function pdfFile(name = "Acme_MSA.pdf") {
  return new File(["%PDF-1.4"], name, { type: "application/pdf" });
}

function renderDocuments(apiClient: ApiClient, initialPath = "/documents") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/documents" element={<DocumentsRoute apiClient={apiClient} />} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/ask" element={<div>ASK_SCREEN</div>} />
        <Route path="/quotes" element={<div>QUOTE_CHECK_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

function selectFiles(files: File[]) {
  const input = screen.getByLabelText(/choose contract files from your computer/i);
  fireEvent.change(input, { target: { files } });
}

function contract360(overrides: Partial<Contract360Body["header"]> = {}): Contract360Body {
  return {
    contractId: "contract-1",
    header: {
      contractId: "contract-1",
      supplierId: null,
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
      ...overrides,
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
      clauses: [],
      obligations: [],
      risks: [],
      documents: [],
      benchmark: [],
      renewal: { endDate: null, renewalDate: null, cancellationDeadline: null, autoRenewal: false, renewalTermMonths: null },
      activity: [],
    },
  };
}

describe("DocumentsRoute (task E13/F09/US01/T03, web-documents-v2)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "contigo.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("guards on no current workspace instead of sending an undefined X-Tenant-Id", () => {
    window.sessionStorage.clear();
    renderDocuments(mockApiClient());

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
  });

  it("shows the onboarding empty state ('First your contracts. Then your questions.') with no tracked documents", async () => {
    renderDocuments(mockApiClient());

    expect(await screen.findByText("First your contracts. Then your questions.")).toBeInTheDocument();
    expect(screen.getByText("01 · Upload")).toBeInTheDocument();
    expect(screen.getByText("02 · Process")).toBeInTheDocument();
    expect(screen.getByText("03 · Ask")).toBeInTheDocument();
  });

  it("dropping three files creates three rows immediately, each reaching its own terminal outcome (R-DOC-01 AC-1)", async () => {
    const uploadDocument = vi
      .fn()
      .mockImplementation(async (_tenantId: string, file: File) => uploadedOk({ id: `id-${file.name}`, fileName: file.name }));
    const listDocuments = vi.fn().mockResolvedValue(emptyPage());
    renderDocuments(mockApiClient({ uploadDocument, listDocuments }));

    await screen.findByText("First your contracts. Then your questions.");
    selectFiles([pdfFile("A.pdf"), pdfFile("B.pdf"), pdfFile("C.pdf")]);

    // Rows exist the moment the files are picked -- before any of the three requests resolve.
    expect(screen.getByText("A.pdf")).toBeInTheDocument();
    expect(screen.getByText("B.pdf")).toBeInTheDocument();
    expect(screen.getByText("C.pdf")).toBeInTheDocument();

    await waitFor(() => expect(uploadDocument).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(listDocuments).toHaveBeenCalled());
  });

  it("a 422 not_a_contract rejection renders a Not added card with the requirements copy, and the summary count stays unchanged", async () => {
    const existing = [docItem({ id: "existing-1" })];
    const uploadDocument = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 422,
      document: null,
      rejection: { rejected: true, detectedType: "Other", confidence: 0.91, reason: "not_a_contract", hint: "..." },
      error: null,
    });
    const listDocuments = vi.fn().mockResolvedValue(listOk(existing));
    renderDocuments(mockApiClient({ uploadDocument, listDocuments }));

    expect(await screen.findByText("1 document · 1 askable")).toBeInTheDocument();

    selectFiles([pdfFile("recipe.pdf")]);

    expect(
      await screen.findByText(
        "Not added: this looks like a recipe, not a contract. Contigo only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("Not added")).toHaveClass("tag");
    // Never becomes a row, and the summary line (server documents only) is unchanged.
    expect(screen.queryByText("recipe.pdf", { selector: ".document-status-table-filename, .document-status-table-link" })).not.toBeInTheDocument();
    expect(screen.getByText("1 document · 1 askable")).toBeInTheDocument();
  });

  it("a 415 rejection renders a Not added card with the server's own format message", async () => {
    const uploadDocument = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 415,
      document: null,
      rejection: null,
      error: "Contigo reads PDF, Word, Excel and scanned images",
    });
    renderDocuments(mockApiClient({ uploadDocument, listDocuments: vi.fn().mockResolvedValue(emptyPage()) }));

    await screen.findByText("First your contracts. Then your questions.");
    selectFiles([pdfFile("archive.zip")]);

    expect(await screen.findByText("Contigo reads PDF, Word, Excel and scanned images")).toBeInTheDocument();
  });

  it("the attention filter hides completed rows and shows the empty message; 'All documents' reveals them", async () => {
    const items = [
      docItem({ id: "a", fileName: "Attention.pdf", processingStatus: "NeedsReview" }),
      docItem({ id: "b", fileName: "Askable.pdf", processingStatus: "Completed" }),
    ];
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)) }));

    expect(await screen.findByText("Attention.pdf")).toBeInTheDocument();
    expect(screen.queryByText("Askable.pdf")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /all documents/i }));

    expect(await screen.findByText("Askable.pdf")).toBeInTheDocument();
  });

  it("shows 'Nothing needs you right now.' when every document is completed", async () => {
    const items = [docItem({ id: "a", processingStatus: "Completed" })];
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)) }));

    expect(await screen.findByText("Nothing needs you right now.")).toBeInTheDocument();
  });

  it("processing rows show the real stage text from the mocked API, not a client timer", async () => {
    const items = [docItem({ id: "p", fileName: "Processing.pdf", processingStatus: "Processing", stage: "Sections & tables" })];
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)) }));

    expect(await screen.findByText("Sections & tables…")).toBeInTheDocument();
  });

  it("polls the list every 2s while a row is non-terminal, and stops once it becomes terminal", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const listDocuments = vi
      .fn<ApiClient["listDocuments"]>()
      .mockResolvedValueOnce(listOk([docItem({ id: "p", processingStatus: "Processing", stage: "Uploading" })]))
      .mockResolvedValueOnce(listOk([docItem({ id: "p", processingStatus: "Processing", stage: "Classifying" })]))
      .mockResolvedValue(listOk([docItem({ id: "p", processingStatus: "Completed" })]));
    renderDocuments(mockApiClient({ listDocuments }));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(listDocuments).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(listDocuments).toHaveBeenCalledTimes(2);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(listDocuments).toHaveBeenCalledTimes(3);

    const callsAfterTerminal = listDocuments.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4000);
    });
    expect(listDocuments).toHaveBeenCalledTimes(callsAfterTerminal); // no further polling once terminal
  });

  it("?review=<id> renders the review state in place of the list", async () => {
    const items = [docItem({ id: "doc-1", contractId: "contract-1", processingStatus: "NeedsReview" })];
    const getContract360 = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      contract: contract360(),
      error: null,
    } satisfies GetContract360Result);
    const getCorrectionHistory = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      history: [] as CorrectionHistoryEntryBody[],
      error: null,
    } satisfies GetCorrectionHistoryResult);
    renderDocuments(
      mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)), getContract360, getCorrectionHistory }),
      "/documents?review=doc-1",
    );

    expect(await screen.findByText("Review extraction")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "← Documents" })).toBeInTheDocument();
  });

  it("Mark as validated returns to the list and shows the 'is now askable' hook with an Ask link", async () => {
    const items = [docItem({ id: "doc-1", contractId: "contract-1", supplierName: "Salesforce", processingStatus: "NeedsReview" })];
    const getContract360 = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      contract: contract360(),
      error: null,
    } satisfies GetContract360Result);
    const getCorrectionHistory = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      history: [],
      error: null,
    } satisfies GetCorrectionHistoryResult);
    renderDocuments(
      mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)), getContract360, getCorrectionHistory }),
      "/documents?review=doc-1",
    );

    await screen.findByText("Review extraction");
    // Every required field (type/status/currency/autoRenewal) starts pending -- accept each so
    // "Mark as validated" is no longer blocked (reviewViewModel.ts's own gate: no live per-field
    // confidence exists yet, so every pending field blocks until a human decision).
    const acceptButtons = screen.getAllByRole("button", { name: "Accept" });
    for (const button of acceptButtons) {
      // eslint-disable-next-line no-await-in-loop
      await userEvent.click(button);
    }

    const markValidated = screen.getByRole("button", { name: "Mark as validated" });
    expect(markValidated).not.toBeDisabled();
    await userEvent.click(markValidated);

    expect(await screen.findByText(/is now askable\./)).toBeInTheDocument();
    const askLink = screen.getByRole("link", { name: "Ask: when does it expire?" });
    expect(askLink).toHaveAttribute("href", "/ask?scope=contract-1");
  });

  it("shows Delete for Admin only", async () => {
    // NeedsReview (not the docItem() default Completed) so this row survives the default
    // "attention" filter (documentTable.ts#isAttentionStatus) -- Delete's own visibility rule
    // (DocumentStatusTable.tsx) is role-only, not status-dependent, so any attention-visible status
    // proves the point; NeedsReview mirrors the convention the attention-filter test above already
    // uses for the same reason.
    const items = [docItem({ processingStatus: "NeedsReview" })];
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)) }));
    await screen.findByText("Salesforce_MSA.pdf");
    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();
  });

  it("hides Delete for Procurement", async () => {
    window.sessionStorage.setItem("contigo.shell.workspaceRole", "procurement");
    // See "shows Delete for Admin only" above for why this is NeedsReview, not the docItem() default.
    const items = [docItem({ processingStatus: "NeedsReview" })];
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)) }));

    await screen.findByText("Salesforce_MSA.pdf");
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
  });
});
