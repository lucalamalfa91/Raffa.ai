import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import type { ApiClient, ReadBackDocument } from "../../../../src/api/client";
import {
  DocumentViewerLink,
  DocumentViewerProvider,
} from "../../../../src/routes/documents/viewer/DocumentViewerOverlay";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";
const DOCUMENT_ID = "55555555-5555-5555-5555-555555555555";

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
    listConversations: vi.fn(),
    createConversation: vi.fn(),
    getConversation: vi.fn(),
    postMessage: vi.fn(),
    postConversationFeedback: vi.fn(),
    deleteConversation: vi.fn(),
    renameConversation: vi.fn(),
    restoreConversation: vi.fn(),
    getCapabilities: vi.fn(),
    getMarketRecord: vi.fn(),
    getQuoteBenchmarkHistory: vi.fn(),
    // Task E29/F04/US01/T01 (todo-web): this suite never reaches the Renewals screen -- bare
    // vi.fn() is enough, same convention as the other unexercised calls above.
    getRenewalNegotiationTodos: vi.fn(),
    tickRenewalNegotiationTodo: vi.fn(),
    listDocuments: vi.fn(),
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
    contractId: "22222222-2222-2222-2222-222222222222",
    fileName: "raffa-sample-northwind-msa.pdf",
    mimeType: "application/pdf",
    documentType: "Msa",
    processingStatus: "Completed",
    createdAt: "2026-09-01T00:00:00Z",
    pageCount: 2,
    isPageCountLimited: false,
    ...overrides,
  };
}

function readyClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return mockApiClient({
    getDocument: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, document: documentRow(), error: null }),
    getDocumentPreviewUrl: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, objectUrl: "blob:page", error: null }),
    getContractEvidence: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, evidence: [], error: null }),
    ...overrides,
  });
}

function renderOverlay(apiClient: ApiClient, href = `/documents/${DOCUMENT_ID}/viewer?page=2`) {
  return render(
    <MemoryRouter>
      <DocumentViewerProvider apiClient={apiClient}>
        <p>List still here</p>
        <DocumentViewerLink to={href} className="btn btn-ghost">
          View document
        </DocumentViewerLink>
      </DocumentViewerProvider>
    </MemoryRouter>,
  );
}

describe("DocumentViewer overlay", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.sessionStorage.setItem(
      "raffa.signin.currentWorkspace",
      JSON.stringify({ id: WORKSPACE_ID, name: "Acme Procurement" }),
    );
  });

  afterEach(() => {
    cleanup();
    document.body.style.overflow = "";
  });

  it("opens as a dialog on the current screen and keeps the underlying page", async () => {
    const apiClient = readyClient();
    const user = userEvent.setup();
    renderOverlay(apiClient);

    expect(screen.getByText("List still here")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: "View document" }));

    const dialog = await screen.findByRole("dialog", { name: "Document viewer" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(await screen.findByText("raffa-sample-northwind-msa.pdf")).toBeInTheDocument();
    expect(screen.getByText("Page 2 of 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Previous" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
    expect(screen.getByText("List still here")).toBeInTheDocument();
  });

  it("closes from the Close control and restores the underlying page", async () => {
    const apiClient = readyClient();
    const user = userEvent.setup();
    renderOverlay(apiClient);

    await user.click(screen.getByRole("link", { name: "View document" }));
    await screen.findByRole("dialog", { name: "Document viewer" });
    await user.click(screen.getByRole("button", { name: "Close" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByText("List still here")).toBeInTheDocument();
  });

  it("closes on Escape", async () => {
    const apiClient = readyClient();
    const user = userEvent.setup();
    renderOverlay(apiClient);

    await user.click(screen.getByRole("link", { name: "View document" }));
    await screen.findByRole("dialog", { name: "Document viewer" });
    await user.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("closes when the dimmed backdrop is clicked", async () => {
    const apiClient = readyClient();
    const user = userEvent.setup();
    renderOverlay(apiClient);

    await user.click(screen.getByRole("link", { name: "View document" }));
    await screen.findByRole("dialog", { name: "Document viewer" });
    await user.click(screen.getByTestId("document-viewer-dialog-backdrop"));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
});
