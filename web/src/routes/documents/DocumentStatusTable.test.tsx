import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import DocumentStatusTable from "./DocumentStatusTable";
import type { DocumentListItemBody } from "../../api/client";
import type { LocalUploadEntry } from "./uploadPipeline";

/**
 * Focused tests for plan instant-upload-open changes in DocumentStatusTable:
 *   1. Local upload row filename is a clickable button that opens the file via blob URL (zero network).
 *   2. Processing rows display quiet chips "Identifying…" / "Enriching…" instead of the old bar.
 *   3. Thumbnail placeholder renders when usePdfThumbnail returns a data URL.
 */

// Mock usePdfThumbnail so tests don't actually invoke PDF.js (no canvas in jsdom).
vi.mock("./usePdfThumbnail", () => ({
  usePdfThumbnail: vi.fn().mockReturnValue(null),
}));

// Mock URL.createObjectURL / URL.revokeObjectURL (not available in jsdom).
const createObjectURL = vi.fn().mockReturnValue("blob:http://localhost/fake-blob");
const revokeObjectURL = vi.fn();

beforeEach(() => {
  vi.stubGlobal("URL", {
    ...URL,
    createObjectURL,
    revokeObjectURL,
  });
  vi.stubGlobal("open", vi.fn());
  createObjectURL.mockClear();
  revokeObjectURL.mockClear();
  (window.open as ReturnType<typeof vi.fn>).mockClear?.();
});

const ZERO_COUNTS = { all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0 };

function makeFile(name = "contract.pdf"): File {
  return new File(["PDF content"], name, { type: "application/pdf" });
}

function makeLocalEntry(overrides: Partial<LocalUploadEntry> = {}): LocalUploadEntry {
  return {
    key: "key-1",
    file: makeFile(),
    phase: "uploading",
    ...overrides,
  };
}

function makeServerDoc(overrides: Partial<DocumentListItemBody> = {}): DocumentListItemBody {
  return {
    id: "doc-1",
    fileName: "agreement.pdf",
    processingStatus: "Processing",
    stage: null,
    pageCount: null,
    createdAt: "2026-01-01T00:00:00Z",
    supplierName: null,
    documentType: "Msa",
    contractId: null,
    weakFactCount: 0,
    rejectionReason: null,
    ...overrides,
  };
}

function renderTable(
  documents: readonly DocumentListItemBody[] = [],
  localUploads: readonly LocalUploadEntry[] = [],
) {
  return render(
    <MemoryRouter>
      <DocumentStatusTable
        documents={documents}
        filter="all"
        localUploads={localUploads}
        onRetryLocal={vi.fn()}
        onRetryServer={vi.fn()}
        onDelete={vi.fn()}
        isAdmin={false}
      />
    </MemoryRouter>,
  );
}

// ─── Local upload row ──────────────────────────────────────────────────────────

describe("local upload row — queued/uploading phase", () => {
  it("renders the filename as a clickable button", () => {
    renderTable([], [makeLocalEntry({ phase: "uploading" })]);
    expect(screen.getByRole("button", { name: "contract.pdf" })).toBeInTheDocument();
  });

  it("clicking the filename calls URL.createObjectURL with the file and opens a new tab", async () => {
    const file = makeFile("my-contract.pdf");
    renderTable([], [makeLocalEntry({ file, phase: "uploading" })]);

    await userEvent.click(screen.getByRole("button", { name: "my-contract.pdf" }));

    expect(createObjectURL).toHaveBeenCalledWith(file);
    expect(window.open).toHaveBeenCalledWith("blob:http://localhost/fake-blob", "_blank", "noopener,noreferrer");
  });

  it("queued phase is also clickable", async () => {
    const file = makeFile("queued.pdf");
    renderTable([], [makeLocalEntry({ file, phase: "queued" })]);

    expect(screen.getByRole("button", { name: "queued.pdf" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "queued.pdf" }));
    expect(createObjectURL).toHaveBeenCalledWith(file);
  });
});

describe("local upload row — failed/rejected phase", () => {
  it("failed: filename is not a button (no file to open without network)", () => {
    renderTable([], [makeLocalEntry({ phase: "failed", errorMessage: "Try again." })]);
    expect(screen.queryByRole("button", { name: "contract.pdf" })).not.toBeInTheDocument();
    expect(screen.getByText("contract.pdf")).toBeInTheDocument();
  });

  it("rejected: filename is not a button", () => {
    renderTable([], [makeLocalEntry({ phase: "rejected", errorMessage: "Too large." })]);
    expect(screen.queryByRole("button", { name: "contract.pdf" })).not.toBeInTheDocument();
  });
});

describe("local upload row — thumbnail", () => {
  it("renders no thumbnail when usePdfThumbnail returns null (loading or non-PDF)", () => {
    renderTable([], [makeLocalEntry()]);
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("renders the thumbnail img when usePdfThumbnail returns a data URL", async () => {
    const { usePdfThumbnail } = await import("./usePdfThumbnail");
    vi.mocked(usePdfThumbnail).mockReturnValue("data:image/jpeg;base64,abc123");

    const { container } = renderTable([], [makeLocalEntry()]);
    // The thumbnail has alt="" + aria-hidden="true" (decorative); use querySelector to avoid
    // the testing-library "presentation" role re-classification for alt="" images.
    const img = container.querySelector(".document-status-table-thumbnail");
    expect(img).not.toBeNull();
    expect(img).toHaveAttribute("src", "data:image/jpeg;base64,abc123");

    // Reset to null for other tests
    vi.mocked(usePdfThumbnail).mockReturnValue(null);
  });
});

// ─── Processing chips ─────────────────────────────────────────────────────────

describe("processing row — quiet chips replace progress bar", () => {
  it("null stage (queued, no Worker) shows 'Identifying…'", () => {
    renderTable([makeServerDoc({ processingStatus: "Processing", stage: null })]);
    expect(screen.getByText("Identifying…")).toBeInTheDocument();
  });

  it("'Classifying' stage shows 'Identifying…'", () => {
    renderTable([makeServerDoc({ processingStatus: "Processing", stage: "Classifying" })]);
    expect(screen.getByText("Identifying…")).toBeInTheDocument();
  });

  it("'OCR / text' stage shows 'Identifying…'", () => {
    renderTable([makeServerDoc({ processingStatus: "Processing", stage: "OCR / text" })]);
    expect(screen.getByText("Identifying…")).toBeInTheDocument();
  });

  it("'Extracting facts' stage shows 'Enriching…' — the former 90 s stuck label", () => {
    renderTable([makeServerDoc({ processingStatus: "Processing", stage: "Extracting facts" })]);
    expect(screen.getByText("Enriching…")).toBeInTheDocument();
    expect(screen.queryByText("Extracting facts…")).not.toBeInTheDocument();
  });

  it("'Validating schema' stage shows 'Enriching…'", () => {
    renderTable([makeServerDoc({ processingStatus: "Processing", stage: "Validating schema" })]);
    expect(screen.getByText("Enriching…")).toBeInTheDocument();
  });

  it("does not render the old progress bar element", () => {
    const { container } = renderTable([makeServerDoc({ processingStatus: "Processing", stage: "Extracting facts" })]);
    expect(container.querySelector(".document-row-progress")).toBeNull();
  });
});
