import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import DocumentsRoute from "../../../src/routes/documents";
import { DocumentViewerProvider } from "../../../src/routes/documents/viewer/DocumentViewerOverlay";
import type { WorkspaceRole } from "../../../src/components/shell/navItems";
import type {
  ApiClient,
  Contract360Body,
  ContractFieldEvidenceBody,
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
    listWorkspaces: vi.fn(),
    getWorkspaceMembers: vi.fn(),
    getWorkspaceSettings: vi.fn(),
    updateWorkspaceSettings: vi.fn(),
    revokeInvitation: vi.fn(),
    removeMember: vi.fn(),
    getInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
    acceptPendingInvitation: vi.fn(),
    uploadDocument: vi.fn(),
    getDocument: vi.fn(),
    // Task E13/F09/US01/T04 (web-ask-v2): this suite never reaches conversations/capabilities/
    // market -- bare vi.fn() is enough, same convention as getContract360 below.
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    postConversationFeedback: vi.fn(),
    deleteConversation: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
    listDocuments: vi.fn().mockResolvedValue(emptyPage()),
    getDocumentPreviewUrl: vi.fn(),
    reprocessDocument: vi.fn(),
    deleteDocument: vi.fn(),
    deleteAllDocuments: vi.fn(),
    prioritiseDocument: vi.fn(),
    getPortfolio: vi.fn(),
    getContract360: vi.fn(),
    getRenewals: vi.fn(),
    getRenewalPriority: vi.fn(),
    getCorrectionHistory: vi.fn(),
    correctContract: vi.fn(),
    getContractEvidence: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, evidence: [], autoAcceptThreshold: 0.87, error: null }),
    getContractStrategy: vi.fn(),
    validateDocument: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      validation: {
        documentId: "doc-1",
        contractId: "contract-1",
        processingStatus: "Completed",
        validatedAt: "2026-09-09T10:00:00Z",
        acceptedFields: [],
        alreadyValidated: false,
      },
      error: null,
    }),
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
    rejectionReason: null,
    errorDetail: null,
    ...overrides,
  };
}

/** Task E16/F02/US03/T01 (ADR-027 §D7, §C5, §C9): the server's tenant-wide counts, derived here
 * from the page the way the backend derives them from the table -- `all` excludes `Rejected`,
 * `needsAttention` is "not Completed and not Rejected", `needsReview` is NeedsReview alone,
 * `processing` is Uploaded + Processing. Overlapping projections, never summed. */
function countsOf(items: DocumentListItemBody[]): DocumentListPageBody["counts"] {
  const of = (...statuses: DocumentListItemBody["processingStatus"][]) =>
    items.filter((item) => statuses.includes(item.processingStatus)).length;
  return {
    all: items.length - of("Rejected"),
    needsAttention: of("Uploaded", "Processing", "NeedsReview", "Failed"),
    needsReview: of("NeedsReview"),
    processing: of("Uploaded", "Processing"),
    rejected: of("Rejected"),
  };
}

function page(items: DocumentListItemBody[]): DocumentListPageBody {
  return { items, page: 1, pageSize: 100, totalCount: items.length, counts: countsOf(items) };
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

function renderDocuments(apiClient: ApiClient, initialPath = "/documents", role: WorkspaceRole = "admin") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <DocumentViewerProvider apiClient={apiClient}>
        <Routes>
          {/* Task E14/F03/US02/T01 (wave w14): role is now threaded through from WorkspaceShellApp's
              ShellRoutes as a prop, instead of this route calling the deleted resolveWorkspaceRole()
              itself -- "admin" by default keeps every pre-existing assertion in this suite unchanged;
              "hides Delete for Procurement" below overrides it. */}
          <Route path="/documents" element={<DocumentsRoute apiClient={apiClient} role={role} />} />
          <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
          <Route path="/ask" element={<div>ASK_SCREEN</div>} />
          <Route path="/quotes" element={<div>QUOTE_CHECK_SCREEN</div>} />
        </Routes>
      </DocumentViewerProvider>
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
    // Task E16/F02/US03/T01 (ADR-027 §D9): a fixture reached from a Completed row is `ready`.
    readiness: { state: "ready", stage: null, documentCount: 1, completedDocumentCount: 1 },
    header: {
      contractId: "contract-1",
      supplierId: null,
      // Task E13/F03/US01/T02: supplierName is required now (null when unresolved).
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

function autoAcceptedEvidence(): ContractFieldEvidenceBody[] {
  return ["type", "status", "currency", "autoRenewal"].map((fieldName) => ({
    fieldName,
    value: "x",
    confidence: 0.96,
    decision: "auto_accepted" as const,
    sourcePage: null,
    sourceSpan: null,
    sourceDocumentId: null,
    sourceFileName: null,
    passage: null,
    highlightStart: null,
    highlightLength: null,
    box: null,
    modelId: null,
    extractedAt: "2026-09-09T10:00:00Z",
  }));
}

describe("DocumentsRoute (task E13/F09/US01/T03, web-documents-v2)", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
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

  // Task E16/F03/US01/T01 (ADR-020 w15 §1, §6; ADR-012 w15 §4, §5, §13.5): every number is the
  // server's `counts`, a refusal is a row, and the optimistic row leaves only on evidence.
  it("the chips and the summary read the server's counts, never the fetched page", async () => {
    // One row on the page, fifteen in the tenant: the chips must never say 1 (or 0).
    const page: DocumentListPageBody = {
      items: [docItem({ id: "a", processingStatus: "Processing", stage: "Classifying" })],
      page: 1,
      pageSize: 100,
      totalCount: 15,
      counts: { all: 15, needsAttention: 15, needsReview: 3, processing: 12, rejected: 0 },
    };
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, page, error: null }) }));

    expect(await screen.findByRole("button", { name: "Needs your attention · 15" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All documents · 15" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Not added ·/ })).toBeNull();
    // The summary line has no page-derived "M askable" segment any more (ADR-027 §C5).
    expect(screen.getByText("15 documents · 3 waiting for your review")).toHaveClass("screen-header-summary");
  });

  it("a server Rejected row lives under the third chip with the requirements sentence, and 'All documents' never counts it", async () => {
    const kept = docItem({ id: "existing-1", fileName: "Salesforce_MSA.pdf", processingStatus: "Completed" });
    const refused = docItem({ id: "refused-1", fileName: "recipe.pdf", processingStatus: "Rejected", contractId: null, rejectionReason: "not_a_contract" });
    const listDocuments = vi.fn().mockImplementation(async (_tenantId: string, query?: { status?: string }) =>
      listOk(query?.status === "Rejected" ? [refused] : [kept, refused]),
    );
    renderDocuments(mockApiClient({ listDocuments }));

    expect(await screen.findByText("1 document")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All documents · 1" })).toBeInTheDocument();
    const chip = screen.getByRole("button", { name: "Not added · 1" });
    expect(screen.queryByText("recipe.pdf")).toBeNull();

    await userEvent.click(chip);

    expect(await screen.findByText("recipe.pdf")).toBeInTheDocument();
    expect(screen.getByText("Not added")).toHaveClass("tag", "tag-outline");
    expect(
      screen.getByText(
        "This looks like a recipe, not a contract. Raffa.ai only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("Files Raffa.ai did not keep — never counted, never askable.")).toBeInTheDocument();
    expect(listDocuments).toHaveBeenCalledWith(WORKSPACE_ID, { status: "Rejected", pageSize: 100 });
    expect(screen.queryByText("Salesforce_MSA.pdf")).toBeNull();
  });

  it("a 415 refusal renders one local 'Not added' row with the designed sentence -- never the server's prose, never a silent drop", async () => {
    const uploadDocument = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 415,
      document: null,
      rejection: null,
      error: "Raffa reads PDF, Word, Excel and scanned images",
    });
    renderDocuments(mockApiClient({ uploadDocument, listDocuments: vi.fn().mockResolvedValue(emptyPage()) }));

    await screen.findByText("First your contracts. Then your questions.");
    selectFiles([pdfFile("archive.zip")]);

    expect(await screen.findByText("Raffa.ai cannot open this file type. Try the original PDF, or a clear scan of the signed pages.")).toBeInTheDocument();
    expect(screen.getByText("archive.zip")).toBeInTheDocument();
    expect(screen.getByText("Not added")).toHaveClass("tag", "tag-outline");
    expect(screen.queryByText("Raffa reads PDF, Word, Excel and scanned images")).toBeNull();
    expect(screen.queryByRole("button", { name: /dismiss/i })).toBeNull();
    expect(screen.queryByText("Uploading…")).toBeNull();
  });

  it("an oversize file renders one local 'Not added' row without any API call", async () => {
    const uploadDocument = vi.fn();
    renderDocuments(mockApiClient({ uploadDocument, listDocuments: vi.fn().mockResolvedValue(emptyPage()) }));

    await screen.findByText("First your contracts. Then your questions.");
    const huge = new File([new Uint8Array(1)], "huge.pdf", { type: "application/pdf" });
    Object.defineProperty(huge, "size", { value: 50 * 1024 * 1024 + 1 });
    selectFiles([huge]);

    expect(await screen.findByText("This file is larger than 50 MB, the most Raffa.ai accepts.")).toBeInTheDocument();
    expect(screen.getByText("huge.pdf")).toBeInTheDocument();
    expect(uploadDocument).not.toHaveBeenCalled();
  });

  it("hands the optimistic row off only once the server list carries its id -- never a gap between drop and reload", async () => {
    const stored = docItem({
      id: "id-A.pdf",
      fileName: "A.pdf",
      processingStatus: "Uploaded",
      stage: null,
      contractId: null,
      createdAt: new Date().toISOString(),
    });
    let listCalls = 0;
    const listDocuments = vi.fn().mockImplementation(async () => {
      listCalls += 1;
      // The initial read is empty; the read the 201 triggers carries the row.
      return listCalls === 1 ? emptyPage() : listOk([stored]);
    });
    const uploadDocument = vi.fn().mockResolvedValue(uploadedOk({ id: "id-A.pdf", fileName: "A.pdf", contractId: null, processingStatus: "Uploaded" }));
    renderDocuments(mockApiClient({ uploadDocument, listDocuments }));

    await screen.findByText("First your contracts. Then your questions.");
    selectFiles([pdfFile("A.pdf")]);

    expect(screen.getByText("A.pdf")).toBeInTheDocument();
    // ADR-020 w15 footer 10 (task E16/F03/US02/T02): the local row and the server row at Uploaded
    // both read "Processing in the background" now (both "Uploaded"), so a bare row count is
    // satisfied trivially by the local row alone, before the server row has even arrived -- the
    // composite check below only passes once the *link* (the server row's own signature; the
    // local row is never a `<Link>`) exists **and** there is exactly one "A.pdf" left, i.e. the
    // server row has replaced the local one, with no moment in between.
    await waitFor(() => {
      expect(screen.getByRole("link", { name: "A.pdf" })).toBeInTheDocument();
      expect(screen.getAllByText("A.pdf")).toHaveLength(1);
    });
    expect(screen.getByText("Processing in the background")).toBeInTheDocument();
    expect(screen.queryByText("Uploading…")).toBeNull();
  });

  it("keeps the optimistic row when the reload after the 201 fails, showing the error inline", async () => {
    let listCalls = 0;
    const listDocuments = vi.fn().mockImplementation(async () => {
      listCalls += 1;
      return listCalls === 1 ? emptyPage() : { ok: false, statusCode: 503, page: null, error: "Service Unavailable" };
    });
    const uploadDocument = vi.fn().mockResolvedValue(uploadedOk({ id: "id-A.pdf", fileName: "A.pdf", contractId: null, processingStatus: "Uploaded" }));
    renderDocuments(mockApiClient({ uploadDocument, listDocuments }));

    await screen.findByText("First your contracts. Then your questions.");
    selectFiles([pdfFile("A.pdf")]);

    await waitFor(() => expect(listDocuments).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole("alert")).toHaveTextContent(/temporarily unavailable/);
    expect(screen.getByText("A.pdf")).toBeInTheDocument();
    // ADR-020 w15 footer 10: the local row (the reload failed, so it never handed off) still reads
    // "Uploaded" / "Processing in the background" -- never "Uploading…" any more.
    expect(screen.getByText("Uploaded")).toHaveClass("tag", "tag-neutral");
    expect(screen.getByText("Processing in the background")).toBeInTheDocument();
    expect(screen.queryByText("Uploading…")).toBeNull();
  });

  it("stops polling after five minutes without a change, keeps informational next-step copy, and resumes on 'Check again'", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const createdAt = new Date().toISOString();
    const reprocessDocument = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 202,
      queued: { documentId: "p", extractionJobId: "job-1", processingStatus: "Uploaded" },
      error: null,
    });
    const listDocuments = vi
      .fn<ApiClient["listDocuments"]>()
      .mockResolvedValue(listOk([docItem({ id: "p", fileName: "Stuck.pdf", processingStatus: "Uploaded", stage: null, contractId: null, createdAt })]));
    renderDocuments(mockApiClient({ listDocuments, reprocessDocument }));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    // ADR-020 w15 footer 10 (task E16/F03/US02/T02): "Uploaded", never "Queued…" -- the row grid's
    // own reading of an `Uploaded` document since the perceived-instant batch.
    expect(await screen.findByText("Processing in the background")).toBeInTheDocument();
    expect(reprocessDocument).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Retry upload" })).not.toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3 * 60_000);
    });
    await waitFor(() => expect(reprocessDocument).toHaveBeenCalledTimes(1));
    expect(reprocessDocument).toHaveBeenCalledWith(WORKSPACE_ID, "p");
    expect(screen.queryByRole("button", { name: "Retry upload" })).not.toBeInTheDocument();
    expect(screen.getByText("Processing in the background")).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2 * 60_000);
    });
    expect(await screen.findByText("Nothing has changed for five minutes, so this page stopped checking for updates.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry upload" })).not.toBeInTheDocument();
    expect(screen.getByText("Processing in the background")).toBeInTheDocument();
    expect(screen.getByText("Uploaded")).toHaveClass("tag");
    expect(screen.queryByText("Failed")).toBeNull();
    expect(reprocessDocument).toHaveBeenCalledTimes(1);

    const callsWhenPaused = listDocuments.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    expect(listDocuments).toHaveBeenCalledTimes(callsWhenPaused);

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Check again" }));
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(listDocuments).toHaveBeenCalledTimes(callsWhenPaused + 1);
    expect(screen.queryByText(/stopped checking for updates/)).toBeNull();
  });

  it("does not auto-reprocess a Processing document at three minutes, including same-label Validating schema", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const reprocessDocument = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 202,
      queued: { documentId: "p", extractionJobId: "job-1", processingStatus: "Uploaded" },
      error: null,
    });
    const listDocuments = vi
      .fn<ApiClient["listDocuments"]>()
      .mockResolvedValue(
        listOk([
          docItem({
            id: "p",
            fileName: "LegalThenRisk.pdf",
            processingStatus: "Processing",
            stage: "Validating schema",
            contractId: null,
            createdAt: new Date().toISOString(),
          }),
        ]),
      );
    renderDocuments(mockApiClient({ listDocuments, reprocessDocument }));

    expect(await screen.findByText("Validating schema…")).toBeInTheDocument();
    expect(reprocessDocument).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3 * 60_000);
    });
    expect(reprocessDocument).not.toHaveBeenCalled();
  });

  it("auto-reprocesses a Processing document stuck on the same stage after fifteen minutes", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const reprocessDocument = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 202,
      queued: { documentId: "p", extractionJobId: "job-1", processingStatus: "Uploaded" },
      error: null,
    });
    const listDocuments = vi
      .fn<ApiClient["listDocuments"]>()
      .mockResolvedValue(
        listOk([
          docItem({
            id: "p",
            fileName: "Hung.pdf",
            processingStatus: "Processing",
            stage: "Uploading",
            contractId: null,
            createdAt: new Date().toISOString(),
          }),
        ]),
      );
    renderDocuments(mockApiClient({ listDocuments, reprocessDocument }));

    expect(await screen.findByText("Uploading…")).toBeInTheDocument();
    expect(reprocessDocument).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3 * 60_000);
    });
    expect(reprocessDocument).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(12 * 60_000);
    });
    await waitFor(() => expect(reprocessDocument).toHaveBeenCalledTimes(1));
    expect(reprocessDocument).toHaveBeenCalledWith(WORKSPACE_ID, "p");
  });

  it("the attention filter hides completed rows and shows the empty message; 'All documents' reveals them", async () => {
    const items = [
      docItem({ id: "a", fileName: "Attention.pdf", processingStatus: "NeedsReview" }),
      docItem({ id: "b", fileName: "Askable.pdf", processingStatus: "Completed" }),
    ];
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)) }));

    expect(await screen.findByText("Attention.pdf")).toBeInTheDocument();
    expect(screen.queryByText("Askable.pdf")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /^All documents/ }));

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

    expect(await screen.findByText(/facts need you/)).toBeInTheDocument();
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
    const client = mockApiClient({
      listDocuments: vi.fn().mockResolvedValue(listOk(items)),
      getContract360,
      getCorrectionHistory,
      getContractEvidence: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        evidence: autoAcceptedEvidence(),
        autoAcceptThreshold: 0.87,
        error: null,
      }),
    });
    renderDocuments(client, "/documents?review=doc-1");

    await screen.findByText(/facts need you/);

    const markValidated = screen.getByRole("button", { name: "Mark as validated" });
    expect(markValidated).not.toBeDisabled();
    await userEvent.click(markValidated);

    expect(await screen.findByText(/is now askable\./)).toBeInTheDocument();
    const askLink = screen.getByRole("link", { name: "Ask: when does it expire?" });
    expect(askLink).toHaveAttribute("href", "/ask?scope=contract-1");
    expect(client.validateDocument).toHaveBeenCalledWith(WORKSPACE_ID, "doc-1", {
      acceptedFields: [],
    });
  });

  it("stays on the review when the sign-off is refused, showing the server's own reason", async () => {
    const items = [docItem({ id: "doc-1", contractId: "contract-1", processingStatus: "NeedsReview" })];
    const getContract360 = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, contract: contract360(), error: null } satisfies GetContract360Result);
    const getCorrectionHistory = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, history: [], error: null } satisfies GetCorrectionHistoryResult);
    const validateDocument = vi.fn().mockResolvedValue({
      ok: false,
      statusCode: 409,
      validation: null,
      error: "This document failed processing; reprocess it before validating.",
    });
    renderDocuments(
      mockApiClient({
        listDocuments: vi.fn().mockResolvedValue(listOk(items)),
        getContract360,
        getCorrectionHistory,
        validateDocument,
        getContractEvidence: vi.fn().mockResolvedValue({
          ok: true,
          statusCode: 200,
          evidence: autoAcceptedEvidence(),
          autoAcceptThreshold: 0.87,
          error: null,
        }),
      }),
      "/documents?review=doc-1",
    );

    await screen.findByText(/facts need you/);
    await userEvent.click(screen.getByRole("button", { name: "Mark as validated" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/failed processing/i);
    expect(screen.getByText(/facts need you/)).toBeInTheDocument();
    expect(screen.queryByText(/is now askable\./)).toBeNull();
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
    // Task E14/F03/US02/T01 (wave w14): role arrives as a prop now (the server's own
    // GET /api/workspaces row, parsed by parseWorkspaceRole), not a sessionStorage mirror this
    // route reads itself -- see "shows Delete for Admin only" above for why this is NeedsReview,
    // not the docItem() default.
    const items = [docItem({ processingStatus: "NeedsReview" })];
    renderDocuments(
      mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)) }),
      "/documents",
      "procurement",
    );

    await screen.findByText("Salesforce_MSA.pdf");
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete all documents" })).not.toBeInTheDocument();
  });

  it("asks Admin to confirm before wiping every document, then reloads the list", async () => {
    const items = [docItem({ processingStatus: "NeedsReview" })];
    const listDocuments = vi.fn().mockResolvedValue(listOk(items));
    const deleteAllDocuments = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
    renderDocuments(mockApiClient({ listDocuments, deleteAllDocuments }));

    await screen.findByText("Salesforce_MSA.pdf");
    await userEvent.click(screen.getByRole("button", { name: "Delete all documents" }));
    expect(deleteAllDocuments).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Confirm delete all" }));
    await waitFor(() => expect(deleteAllDocuments).toHaveBeenCalledWith(WORKSPACE_ID));
    await waitFor(() => expect(listDocuments.mock.calls.length).toBeGreaterThan(1));
  });

  it("View document opens the viewer overlay on the list without leaving /documents", async () => {
    const items = [docItem({ id: "doc-1", fileName: "raffa-sample-northwind-msa.pdf", processingStatus: "NeedsReview", weakFactCount: 2 })];
    const getDocument = vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      document: {
        id: "doc-1",
        contractId: "contract-1",
        fileName: "raffa-sample-northwind-msa.pdf",
        mimeType: "application/pdf",
        documentType: "Msa",
        processingStatus: "Completed",
        createdAt: "2026-09-06T08:05:00Z",
        pageCount: 2,
        isPageCountLimited: false,
      },
      error: null,
    });
    const getDocumentPreviewUrl = vi.fn().mockResolvedValue({ ok: true, statusCode: 200, objectUrl: "blob:page", error: null });
    renderDocuments(mockApiClient({ listDocuments: vi.fn().mockResolvedValue(listOk(items)), getDocument, getDocumentPreviewUrl }));

    await screen.findByText("raffa-sample-northwind-msa.pdf");
    await userEvent.click(screen.getByRole("link", { name: "View document" }));

    expect(await screen.findByRole("dialog", { name: "Document viewer" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Documents" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "raffa-sample-northwind-msa.pdf" })).toBeInTheDocument();
  });
});
