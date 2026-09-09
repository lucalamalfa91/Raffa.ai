import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import DocumentStatusTable from "../../../src/routes/documents/DocumentStatusTable";
import type { DocumentListItemBody } from "../../../src/api/client";
import type { LocalUploadEntry, RejectedFileOutcome } from "../../../src/routes/documents/uploadPipeline";

function item(overrides: Partial<DocumentListItemBody> = {}): DocumentListItemBody {
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

function renderTable(props: Partial<ComponentProps<typeof DocumentStatusTable>> = {}) {
  return render(
    <MemoryRouter>
      <DocumentStatusTable
        documents={[]}
        filter="attention"
        localUploads={[]}
        rejected={[]}
        onDismissRejected={vi.fn()}
        onRetryLocal={vi.fn()}
        onRetryServer={vi.fn()}
        onDelete={vi.fn()}
        isAdmin={false}
        {...props}
      />
    </MemoryRouter>,
  );
}

describe("DocumentStatusTable", () => {
  it("renders the four locked columns for a non-admin viewer", () => {
    renderTable({ documents: [item()] });

    const headers = screen.getAllByRole("columnheader").map((header) => header.textContent);
    expect(headers).toEqual(["Document", "Supplier · type", "Status", "Next step"]);
  });

  it("adds a fifth Delete column only for Admin (R-WEB-07)", () => {
    renderTable({ documents: [item()], isAdmin: true });

    expect(screen.getAllByRole("columnheader").map((header) => header.textContent)).toEqual([
      "Document",
      "Supplier · type",
      "Status",
      "Next step",
      "Delete",
    ]);
    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();
  });

  it("does not offer Delete to Procurement", () => {
    renderTable({ documents: [item()], isAdmin: false });

    expect(screen.queryByText("Delete")).not.toBeInTheDocument();
  });

  it("links a completed row's filename to Contract 360 and offers 'Ask about it'", () => {
    renderTable({ documents: [item({ processingStatus: "Completed" })] });

    expect(screen.getByRole("link", { name: "Salesforce_MSA.pdf" })).toHaveAttribute("href", "/contracts/contract-1");
    const askLink = screen.getByRole("link", { name: "Ask about it" });
    expect(askLink).toHaveAttribute("href", "/ask?scope=contract-1");
  });

  it("links a needs_review row to the review state, with the real weak-fact count", () => {
    renderTable({ documents: [item({ processingStatus: "NeedsReview", weakFactCount: 2 })] });

    expect(screen.getByRole("link", { name: "Salesforce_MSA.pdf" })).toHaveAttribute("href", "/documents?review=doc-1");
    expect(screen.getByRole("link", { name: "Review 2 fields" })).toHaveAttribute("href", "/documents?review=doc-1");
  });

  it("routes a Quote-typed row to Quote check instead of review/ask (OQ-askv2-008)", () => {
    renderTable({ documents: [item({ documentType: "Quote", processingStatus: "Completed" })] });

    expect(screen.getByRole("link", { name: "Salesforce_MSA.pdf" })).toHaveAttribute("href", "/quotes");
    expect(screen.getByRole("link", { name: "Open Quote check" })).toHaveAttribute("href", "/quotes");
  });

  it("shows the real stage and a progress bar for a processing row, no action button", () => {
    renderTable({ documents: [item({ processingStatus: "Processing", stage: "OCR / text" })] });

    expect(screen.getByText("OCR / text…")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("offers Retry upload for a server-known failed row, calling onRetryServer with its id", async () => {
    const onRetryServer = vi.fn();
    renderTable({ documents: [item({ id: "doc-9", processingStatus: "Failed" })], onRetryServer });

    await userEvent.click(screen.getByRole("button", { name: "Retry upload" }));

    expect(onRetryServer).toHaveBeenCalledWith("doc-9");
  });

  it("renders a local in-flight upload as its own row from the moment it is picked (R-DOC-01 AC-1)", () => {
    const localUploads: LocalUploadEntry[] = [
      { key: "local-1", file: new File(["x"], "New.pdf", { type: "application/pdf" }), phase: "uploading" },
    ];
    renderTable({ localUploads });

    expect(screen.getByText("New.pdf")).toBeInTheDocument();
    expect(screen.getByText("Uploading…")).toBeInTheDocument();
  });

  it("offers Retry upload for a local failed upload, calling onRetryLocal with its key", async () => {
    const onRetryLocal = vi.fn();
    const localUploads: LocalUploadEntry[] = [
      {
        key: "local-1",
        file: new File(["x"], "Broken.pdf", { type: "application/pdf" }),
        phase: "failed",
        errorMessage: "Contigo could not process Broken.pdf. Try again.",
      },
    ];
    renderTable({ localUploads, onRetryLocal });

    expect(screen.getByText("Contigo could not process Broken.pdf. Try again.")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Retry upload" }));

    expect(onRetryLocal).toHaveBeenCalledWith("local-1");
  });

  it("renders every rejected file as a dismissible 'Not added' card, separate from the row grid", async () => {
    const onDismissRejected = vi.fn();
    const rejected: RejectedFileOutcome[] = [{ key: "r-1", fileName: "recipe.pdf", message: "Not added: this looks like a recipe..." }];
    renderTable({ rejected, onDismissRejected });

    expect(screen.getByText("Not added: this looks like a recipe...")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /dismiss/i }));
    expect(onDismissRejected).toHaveBeenCalledWith("r-1");
  });

  it("shows 'Nothing needs you right now.' only when the attention filter is truly empty", () => {
    renderTable({ documents: [], localUploads: [], filter: "attention" });

    expect(screen.getByText("Nothing needs you right now.")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("does not show the attention-empty message when the all filter is active", () => {
    renderTable({ documents: [], localUploads: [], filter: "all" });

    expect(screen.queryByText("Nothing needs you right now.")).not.toBeInTheDocument();
  });

  it("shows the supplier name and type label together", () => {
    renderTable({ documents: [item({ supplierName: "Microsoft", documentType: "OrderForm" })] });

    const row = screen.getAllByRole("row")[1];
    expect(within(row).getByText("Microsoft")).toBeInTheDocument();
    expect(within(row).getByText("· Order Form")).toBeInTheDocument();
  });

  it("shows an honest '—' when the supplier is not yet resolved", () => {
    renderTable({ documents: [item({ supplierName: null })] });

    expect(screen.getByText("—")).toBeInTheDocument();
  });
});
