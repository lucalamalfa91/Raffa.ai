import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import DocumentProgressPanel from "../../../src/routes/documents/DocumentProgressPanel";
import type { ApiClient, DocumentListItemBody } from "../../../src/api/client";

function item(overrides: Partial<DocumentListItemBody> = {}): DocumentListItemBody {
  return {
    id: "doc-1",
    contractId: "contract-1",
    supplierName: "Salesforce",
    fileName: "Salesforce_MSA.pdf",
    documentType: "Msa",
    processingStatus: "Uploaded",
    stage: null,
    pageCount: 12,
    createdAt: "2026-09-06T08:05:00Z",
    weakFactCount: 0,
    rejectionReason: null,
    ...overrides,
  };
}

function mockApiClient(prioritiseDocument: ApiClient["prioritiseDocument"]): ApiClient {
  return { prioritiseDocument } as unknown as ApiClient;
}

// Task E16/F03/US02/T02 (wave w15, ADR-020 w15 footer 11; ADR-027 w15 footer C12).
describe("DocumentProgressPanel", () => {
  it("calls prioritiseDocument exactly once for this document on mount", () => {
    const prioritiseDocument = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
    render(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(prioritiseDocument)}
          tenantId="tenant-1"
          item={item()}
          onBack={vi.fn()}
          updatesPaused={false}
          onResumeUpdates={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(prioritiseDocument).toHaveBeenCalledTimes(1);
    expect(prioritiseDocument).toHaveBeenCalledWith("tenant-1", "doc-1");
  });

  it("does not call prioritiseDocument again when the same document re-renders with a fresher poll read", () => {
    const prioritiseDocument = vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null });
    const { rerender } = render(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(prioritiseDocument)}
          tenantId="tenant-1"
          item={item({ stage: null })}
          onBack={vi.fn()}
          updatesPaused={false}
          onResumeUpdates={vi.fn()}
        />
      </MemoryRouter>,
    );

    // A poll tick hands in a fresh object, same id, further along -- the exact shape
    // `useDocumentsList.ts` re-renders this component with every 2 s.
    rerender(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(prioritiseDocument)}
          tenantId="tenant-1"
          item={item({ processingStatus: "Processing", stage: "Classifying" })}
          onBack={vi.fn()}
          updatesPaused={false}
          onResumeUpdates={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(prioritiseDocument).toHaveBeenCalledTimes(1);
  });

  // Updated for plan instant-upload-open: the 6-stage checklist is replaced by two quiet chips.
  it("shows the two processing chips while waiting, with the active chip reflecting the stage", () => {
    render(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null }))}
          tenantId="tenant-1"
          item={item({ processingStatus: "Processing", stage: "OCR / text" })}
          onBack={vi.fn()}
          updatesPaused={false}
          onResumeUpdates={vi.fn()}
        />
      </MemoryRouter>,
    );

    // Both chips are present; OCR / text is an early stage → Identifying… is active
    expect(screen.getByText("Identifying…")).toBeInTheDocument();
    expect(screen.getByText("Enriching…")).toBeInTheDocument();
    expect(screen.getByText("Raffa.ai is giving this document priority over the rest of the queue.")).toBeInTheDocument();
    // The old 6-stage list is gone
    expect(screen.queryByRole("list", { name: "Processing stages" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("shows Enriching… as active chip for a late-stage ('Extracting facts') document", () => {
    render(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null }))}
          tenantId="tenant-1"
          item={item({ processingStatus: "Processing", stage: "Extracting facts" })}
          onBack={vi.fn()}
          updatesPaused={false}
          onResumeUpdates={vi.fn()}
        />
      </MemoryRouter>,
    );

    const enrichingChip = screen.getByText("Enriching…");
    expect(enrichingChip).toBeInTheDocument();
    expect(enrichingChip.closest(".documents-progress-chip--active")).not.toBeNull();
    // The headline still shows the stage name as status context; what is gone is the old
    // 6-stage checklist and the raw stage text in the next-step cell row.
    expect(screen.queryByRole("list", { name: "Processing stages" })).not.toBeInTheDocument();
  });

  it("drops the chips and offers the review link once the document needs review", () => {
    render(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null }))}
          tenantId="tenant-1"
          item={item({ id: "doc-4", processingStatus: "NeedsReview" })}
          onBack={vi.fn()}
          updatesPaused={false}
          onResumeUpdates={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(screen.queryByText("Identifying…")).not.toBeInTheDocument();
    expect(screen.queryByText("Enriching…")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review now" })).toHaveAttribute("href", "/documents?review=doc-4");
  });

  it("shows the stopped-updates notice with 'Check again' only while waiting", async () => {
    const onResumeUpdates = vi.fn();
    render(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null }))}
          tenantId="tenant-1"
          item={item({ processingStatus: "Uploaded", stage: null })}
          onBack={vi.fn()}
          updatesPaused={true}
          onResumeUpdates={onResumeUpdates}
        />
      </MemoryRouter>,
    );

    expect(screen.getByText("Nothing has changed for five minutes, so this page stopped checking for updates.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Check again" }));
    expect(onResumeUpdates).toHaveBeenCalledTimes(1);
  });

  it("calls onBack from the '← Documents' control", async () => {
    const onBack = vi.fn();
    render(
      <MemoryRouter>
        <DocumentProgressPanel
          apiClient={mockApiClient(vi.fn().mockResolvedValue({ ok: true, statusCode: 204, error: null }))}
          tenantId="tenant-1"
          item={item()}
          onBack={onBack}
          updatesPaused={false}
          onResumeUpdates={vi.fn()}
        />
      </MemoryRouter>,
    );

    await userEvent.click(screen.getByRole("button", { name: "← Documents" }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });
});
