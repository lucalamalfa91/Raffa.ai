import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import DocumentStatusTable from "../../../src/routes/documents/DocumentStatusTable";
import type { DocumentListItemBody } from "../../../src/api/client";
import type { LocalUploadEntry } from "../../../src/routes/documents/uploadPipeline";

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
    rejectionReason: null,
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

  it("shows the real stage and a progress bar for a processing row, no action button, filename opens the progress panel", () => {
    renderTable({ documents: [item({ id: "doc-1", processingStatus: "Processing", stage: "OCR / text" })] });

    expect(screen.getByText("OCR / text…")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toBeInTheDocument();
    // The only link on the row is the filename, opening the progress panel -- the next-step cell
    // has the stage text, never an action button, while a Worker is on it.
    expect(screen.getAllByRole("link")).toHaveLength(1);
    expect(screen.getByRole("link", { name: "Salesforce_MSA.pdf" })).toHaveAttribute("href", "/documents?progress=doc-1");
  });

  // ADR-020 w15 footer 10 (task E16/F03/US02/T02, wave w15): a stored server row at `Uploaded`
  // reads "Uploaded", not "Queued…" and not a bar -- the perceived-instant batch. Its filename
  // still opens the progress panel (footer 11), which is where the real stage checklist lives.
  it("reads 'Uploaded' with no bar for a server row at Uploaded, filename opens the progress panel", () => {
    renderTable({
      documents: [
        item({
          id: "doc-1",
          processingStatus: "Uploaded",
          stage: null,
          contractId: null,
          createdAt: new Date().toISOString(),
        }),
      ],
    });

    expect(screen.getByText("Uploaded")).toHaveClass("tag", "tag-neutral");
    expect(screen.getByText("Processing in the background")).toBeInTheDocument();
    expect(screen.queryByText("Queued…")).not.toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Salesforce_MSA.pdf" })).toHaveAttribute("href", "/documents?progress=doc-1");
  });

  it("keeps 'Processing in the background' for an Uploaded row older than three minutes, with no Retry upload CTA", () => {
    renderTable({
      documents: [
        item({
          id: "doc-stuck",
          processingStatus: "Uploaded",
          stage: null,
          contractId: null,
          createdAt: new Date(Date.now() - 4 * 60_000).toISOString(),
        }),
      ],
    });

    expect(screen.getByText("Processing in the background")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry upload" })).not.toBeInTheDocument();
  });

  it("does not offer Retry upload for a server-known failed row", () => {
    renderTable({ documents: [item({ id: "doc-9", processingStatus: "Failed" })] });

    expect(screen.queryByRole("button", { name: "Retry upload" })).not.toBeInTheDocument();
  });

  // Task E16/F03/US02/T02 (ADR-020 w15 footer 10, wave w15): the perceived-instant batch -- a row
  // reads "Uploaded", never "Uploading…" and never a bar, from the moment a file is picked, before
  // the POST has even resolved (R-DOC-01 AC-1). No `<Link>` either: a local entry has no server id
  // yet for the progress panel to look up.
  it("renders a local in-flight upload as its own 'Uploaded' row from the moment it is picked (R-DOC-01 AC-1)", () => {
    const localUploads: LocalUploadEntry[] = [
      { key: "local-1", file: new File(["x"], "New.pdf", { type: "application/pdf" }), phase: "uploading" },
    ];
    renderTable({ localUploads });

    expect(screen.getByText("New.pdf")).toBeInTheDocument();
    expect(screen.getByText("Uploaded")).toHaveClass("tag", "tag-neutral");
    expect(screen.getByText("Processing in the background")).toBeInTheDocument();
    expect(screen.queryByText("Uploading…")).not.toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("renders a local failed upload with its sentence and no Retry upload CTA", () => {
    const localUploads: LocalUploadEntry[] = [
      {
        key: "local-1",
        file: new File(["x"], "Broken.pdf", { type: "application/pdf" }),
        phase: "failed",
        errorMessage: "Raffa.ai could not process Broken.pdf. Try again.",
      },
    ];
    renderTable({ localUploads });

    expect(screen.getByText("Raffa.ai could not process Broken.pdf. Try again.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry upload" })).not.toBeInTheDocument();
  });

  // ADR-020 w15 §6 / ADR-019 w15 clause 4: a refusal that never reached the server (oversize,
  // 413, 415) is a *local row* reading "Not added" -- the third branch of the local-entry mapping
  // the compiler never asks for -- with its designed sentence as the hint, no next step, no stage
  // bar, no "Uploading…", and no dismiss.
  it("renders a local refusal as a 'Not added' row with its sentence and nothing to do next", () => {
    const localUploads: LocalUploadEntry[] = [
      {
        key: "local-1",
        file: new File(["x"], "huge.pdf", { type: "application/pdf" }),
        phase: "rejected",
        errorMessage: "This file is larger than 50 MB, the most Raffa.ai accepts.",
      },
    ];
    renderTable({ localUploads, isAdmin: true });

    const row = screen.getAllByRole("row")[1];
    expect(within(row).getByText("huge.pdf")).toBeInTheDocument();
    expect(within(row).getByText("Not added")).toHaveClass("tag", "tag-outline");
    expect(within(row).getByText("This file is larger than 50 MB, the most Raffa.ai accepts.")).toBeInTheDocument();
    expect(within(row).queryByText("Uploading…")).not.toBeInTheDocument();
    expect(within(row).queryByText("Processing")).not.toBeInTheDocument();
    expect(within(row).queryByRole("progressbar")).not.toBeInTheDocument();
    expect(within(row).queryByRole("button")).not.toBeInTheDocument();
  });

  // ADR-020 w15 §1.2-§1.4 / §7: the server's `Rejected` row -- the same "Not added" tag, the
  // requirements sentence selected by the reason *code*, an empty action cell, and the Admin's
  // existing Delete as the only way to clear it.
  it("renders a server Rejected row as 'Not added' with the reason's own sentence and no next step, still deletable by Admin", () => {
    renderTable({
      documents: [item({ id: "doc-7", fileName: "carbonara.pdf", processingStatus: "Rejected", contractId: null, rejectionReason: "not_a_contract" })],
      filter: "rejected",
      isAdmin: true,
    });

    const row = screen.getAllByRole("row")[1];
    expect(within(row).getByText("carbonara.pdf")).toBeInTheDocument();
    expect(within(row).queryByRole("link")).not.toBeInTheDocument();
    expect(within(row).getByText("Not added")).toHaveClass("tag", "tag-outline");
    expect(within(row).getByText(/This looks like a recipe, not a contract\./)).toBeInTheDocument();
    expect(within(row).getByText(/This looks like a recipe/).textContent).not.toMatch(/^Not added:/);
    expect(within(row).queryByText(/Retry|Review|Ask about it|Open Quote check/)).not.toBeInTheDocument();
    expect(within(row).getByRole("button", { name: "Delete" })).toBeInTheDocument();
  });

  it("stays silent -- tag, no hint -- for a Rejected row whose reason code this app does not know", () => {
    renderTable({ documents: [item({ processingStatus: "Rejected", contractId: null, rejectionReason: null })], filter: "rejected" });

    const row = screen.getAllByRole("row")[1];
    expect(within(row).getByText("Not added")).toBeInTheDocument();
    expect(row.querySelector(".hint")).toBeNull();
  });

  it("does not render local rows under the 'Not added' chip, which reads the server bucket alone", () => {
    const localUploads: LocalUploadEntry[] = [
      { key: "local-1", file: new File(["x"], "New.pdf", { type: "application/pdf" }), phase: "uploading" },
    ];
    renderTable({ localUploads, filter: "rejected", documents: [item({ processingStatus: "Rejected", contractId: null })] });

    expect(screen.queryByText("New.pdf")).not.toBeInTheDocument();
    expect(screen.getByText("Salesforce_MSA.pdf")).toBeInTheDocument();
  });

  // ADR-020 w15 §8: a list-level notice, never a row state -- rows and tags are untouched, and
  // the one control resumes the poll.
  it("renders the stopped-updates notice below the grid with 'Check again', leaving every row as it was", async () => {
    const onResumeUpdates = vi.fn();
    renderTable({
      documents: [item({ processingStatus: "Uploaded", stage: null, contractId: null, createdAt: new Date().toISOString() })],
      updatesPaused: true,
      onResumeUpdates,
    });

    expect(screen.getByText("Nothing has changed for five minutes, so this page stopped checking for updates.")).toHaveClass("hint");
    expect(screen.getByText("Processing in the background")).toBeInTheDocument();
    expect(screen.getByText("Uploaded")).toHaveClass("tag");
    await userEvent.click(screen.getByRole("button", { name: "Check again" }));
    expect(onResumeUpdates).toHaveBeenCalledTimes(1);
  });

  it("renders no notice while updates are running", () => {
    renderTable({
      documents: [item({ processingStatus: "Uploaded", stage: null, contractId: null, createdAt: new Date().toISOString() })],
    });

    expect(screen.queryByText(/stopped checking for updates/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Check again" })).not.toBeInTheDocument();
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

  it("offers Confirm delete and Cancel as an inline pair, then calls onDelete only on confirm", async () => {
    const onDelete = vi.fn();
    renderTable({ documents: [item()], isAdmin: true, onDelete });

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));

    const confirm = screen.getByRole("button", { name: "Confirm delete" });
    const cancel = screen.getByRole("button", { name: "Cancel" });
    expect(confirm).toHaveClass("btn-primary");
    expect(cancel).toHaveClass("document-status-table-delete-cancel");
    expect(confirm.parentElement).toHaveClass("document-status-table-delete-confirm");
    expect(onDelete).not.toHaveBeenCalled();

    await userEvent.click(cancel);
    expect(onDelete).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirm delete" }));
    expect(onDelete).toHaveBeenCalledWith("doc-1");
  });
});
