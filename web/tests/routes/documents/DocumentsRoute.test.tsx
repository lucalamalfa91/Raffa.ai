import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import DocumentsRoute from "../../../src/routes/documents";
import { PIPELINE_STEP_INTERVAL_MS } from "../../../src/routes/documents/uploadPipeline";
import { SAMPLE_DOCUMENT_FILE_NAME } from "../../../src/routes/documents/sampleDocument";
import type { ApiClient, UploadDocumentResult, UploadedDocument } from "../../../src/api/client";

const WORKSPACE_ID = "11111111-1111-1111-1111-111111111111";

function mockApiClient(uploadDocument: ApiClient["uploadDocument"] = vi.fn()): ApiClient {
  return { getHealth: vi.fn(), createWorkspace: vi.fn(), uploadDocument };
}

function uploaded(overrides: Partial<UploadedDocument> = {}): UploadedDocument {
  return {
    id: "doc-1",
    contractId: "contract-1",
    fileName: "Acme_MSA.pdf",
    mimeType: "application/pdf",
    processingStatus: "Completed",
    createdAt: "2026-09-06T08:00:00Z",
    ...overrides,
  };
}

function ok(document: UploadedDocument): UploadDocumentResult {
  return { ok: true, statusCode: 201, document, error: null };
}

function renderDocuments(apiClient: ApiClient, initialPath = "/documents") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/documents" element={<DocumentsRoute apiClient={apiClient} />} />
        <Route path="/contracts" element={<div>PORTFOLIO_SCREEN</div>} />
        <Route path="/contracts/:contractId" element={<div>CONTRACT_360_SCREEN</div>} />
        <Route path="/contracts/:contractId/review" element={<div>FIELD_REVIEW_SCREEN</div>} />
        <Route path="/review" element={<div>REVIEW_QUEUE_SCREEN</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

function selectFile(file: File) {
  const input = screen.getByLabelText(/choose contract files from your computer/i);
  fireEvent.change(input, { target: { files: [file] } });
}

function pdfFile(name = "Acme_MSA.pdf") {
  return new File(["%PDF-1.4"], name, { type: "application/pdf" });
}

describe("DocumentsRoute", () => {
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
    const uploadDocument = vi.fn();

    renderDocuments(mockApiClient(uploadDocument));

    expect(screen.getByText(/no workspace selected/i)).toBeInTheDocument();
    expect(screen.queryByText("Drop contracts here")).not.toBeInTheDocument();
  });

  it("shows the pipeline immediately, then the result card once uploadDocument resolves", async () => {
    const uploadDocument = vi.fn().mockResolvedValue(ok(uploaded({ processingStatus: "Completed" })));
    renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile());

    expect(uploadDocument).toHaveBeenCalledWith(WORKSPACE_ID, expect.any(File));
    expect(screen.getByRole("status")).toBeInTheDocument(); // pipeline region
    expect(screen.getByText("Uploaded to object storage")).toBeInTheDocument();

    expect(await screen.findByRole("button", { name: "Open Contract 360" })).toBeInTheDocument();
    expect(screen.getByText("Completed")).toBeInTheDocument();
    expect(screen.getByText("Acme_MSA.pdf")).toBeInTheDocument();
  });

  it("renders the needs_review outcome and routes its CTA to the contract's review route", async () => {
    const uploadDocument = vi.fn().mockResolvedValue(
      ok(uploaded({ processingStatus: "NeedsReview", contractId: "contract-9" })),
    );
    renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile());

    clickButton(await screen.findByRole("button", { name: "Review extraction" }));

    expect(await screen.findByText("FIELD_REVIEW_SCREEN")).toBeInTheDocument();
  });

  it("renders the completed outcome and routes its CTA to Contract 360", async () => {
    const uploadDocument = vi.fn().mockResolvedValue(
      ok(uploaded({ processingStatus: "Completed", contractId: "contract-9" })),
    );
    renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile());

    clickButton(await screen.findByRole("button", { name: "Open Contract 360" }));

    expect(await screen.findByText("CONTRACT_360_SCREEN")).toBeInTheDocument();
  });

  it("renders a failed result (processingStatus: Failed) with a Retry that re-uploads the same file", async () => {
    const uploadDocument = vi.fn().mockResolvedValueOnce(ok(uploaded({ processingStatus: "Failed" })));
    renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile("Acme_MSA.pdf"));
    expect(await screen.findByRole("button", { name: "Retry upload" })).toBeInTheDocument();
    expect(screen.getByText(/could not process Acme_MSA\.pdf/i)).toBeInTheDocument();

    uploadDocument.mockResolvedValueOnce(ok(uploaded({ processingStatus: "Completed" })));
    clickButton(screen.getByRole("button", { name: "Retry upload" }));

    expect(uploadDocument).toHaveBeenCalledTimes(2);
    const [, secondCallFile] = uploadDocument.mock.calls[1];
    expect(secondCallFile.name).toBe("Acme_MSA.pdf");
    expect(await screen.findByRole("button", { name: "Open Contract 360" })).toBeInTheDocument();
  });

  it("renders a failed result on a network/HTTP error, surfacing the client's own error text", async () => {
    const uploadDocument = vi
      .fn()
      .mockResolvedValue({ ok: false, statusCode: 400, document: null, error: "The file field is required." });
    renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile());

    expect(await screen.findByText("The file field is required.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry upload" })).toBeInTheDocument();
  });

  it("'Upload another' clears the result and re-enables the dropzone", async () => {
    const uploadDocument = vi.fn().mockResolvedValue(ok(uploaded({ processingStatus: "Completed" })));
    renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile());
    await screen.findByRole("button", { name: "Open Contract 360" });

    clickButton(screen.getByRole("button", { name: "Upload another" }));

    expect(screen.queryByRole("button", { name: "Open Contract 360" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Choose from computer" })).not.toBeDisabled();
  });

  it("queues a second file selected mid-upload and starts it automatically on 'Upload another'", async () => {
    const uploadDocument = vi
      .fn()
      .mockResolvedValueOnce(ok(uploaded({ processingStatus: "Completed", fileName: "First.pdf" })))
      .mockResolvedValueOnce(ok(uploaded({ processingStatus: "Completed", fileName: "Second.pdf" })));
    renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile("First.pdf"));
    // Still uploading (result card not shown yet) -- select a second file now.
    selectFile(pdfFile("Second.pdf"));
    expect(uploadDocument).toHaveBeenCalledTimes(1); // second file only queued, not started yet

    await screen.findByRole("button", { name: "Open Contract 360" });
    expect(screen.getByText("First.pdf")).toBeInTheDocument();

    clickButton(screen.getByRole("button", { name: "Upload another" }));

    expect(uploadDocument).toHaveBeenCalledTimes(2);
    expect(await screen.findByText("Second.pdf")).toBeInTheDocument();
  });

  it("'Use sample file' uploads the synthetic sample through the same real upload path", async () => {
    const uploadDocument = vi.fn().mockResolvedValue(ok(uploaded({ processingStatus: "Completed" })));
    renderDocuments(mockApiClient(uploadDocument));

    clickButton(screen.getByRole("button", { name: "Use sample file" }));

    expect(uploadDocument).toHaveBeenCalledTimes(1);
    const [tenantId, file] = uploadDocument.mock.calls[0];
    expect(tenantId).toBe(WORKSPACE_ID);
    expect(file.name).toBe(SAMPLE_DOCUMENT_FILE_NAME);
    expect(await screen.findByRole("button", { name: "Open Contract 360" })).toBeInTheDocument();
  });

  it("advances the 6-stage pipeline on a fixed cadence while the request is still in flight", async () => {
    vi.useFakeTimers();
    let resolveUpload!: (value: UploadDocumentResult) => void;
    const pending = new Promise<UploadDocumentResult>((resolve) => {
      resolveUpload = resolve;
    });
    const uploadDocument = vi.fn().mockReturnValue(pending);
    const { container } = renderDocuments(mockApiClient(uploadDocument));

    selectFile(pdfFile());
    expect(container.querySelector(".pipeline-stage--current")).toHaveTextContent("Uploaded to object storage");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(PIPELINE_STEP_INTERVAL_MS * 2);
    });
    expect(container.querySelector(".pipeline-stage--current")).toHaveTextContent("Text extraction / OCR");

    await act(async () => {
      resolveUpload(ok(uploaded({ processingStatus: "Completed" })));
      // Flush the resolved promise's microtask without advancing real
      // interval time any further -- the request's own resolution should
      // win over the pacing ticker regardless of which stage it reached.
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(screen.getByRole("button", { name: "Open Contract 360" })).toBeInTheDocument();
  });
});

function clickButton(element: HTMLElement) {
  fireEvent.click(element);
}
