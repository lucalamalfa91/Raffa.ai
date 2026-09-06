import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import DocumentStatusTable from "../../../src/routes/documents/DocumentStatusTable";
import type { TrackedDocument } from "../../../src/routes/documents/documentStore";

function trackedDocument(overrides: Partial<TrackedDocument> = {}): TrackedDocument {
  return {
    id: "doc-1",
    contractId: "contract-1",
    fileName: "Acme_MSA.pdf",
    documentType: "Msa",
    processingStatus: "Completed",
    createdAt: "2026-09-06T08:05:00Z",
    ...overrides,
  };
}

function renderTable(documents: TrackedDocument[]) {
  return render(
    <MemoryRouter>
      <DocumentStatusTable documents={documents} />
    </MemoryRouter>,
  );
}

// AC-1 "Document table: Document / Type / Supplier / Status / Uploaded" /
// AC-2 (status tags, ADR-019) / AC-3 (row cross-link, ia.md "Document row ->
// Contract 360").
describe("DocumentStatusTable", () => {
  it("renders the empty state when no document has been tracked yet", () => {
    renderTable([]);

    expect(screen.getByText("No documents yet")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("renders the five locked columns in order (AC-1)", () => {
    renderTable([trackedDocument()]);

    const headers = screen.getAllByRole("columnheader").map((header) => header.textContent);
    expect(headers).toEqual(["Document", "Type", "Supplier", "Status", "Uploaded"]);
  });

  it("renders one row per document with Document/Type/Status/Uploaded populated (AC-1, AC-2)", () => {
    renderTable([trackedDocument({ documentType: "OrderForm", processingStatus: "NeedsReview" })]);

    const row = screen.getAllByRole("row")[1]; // row[0] is the header row
    const cells = within(row)
      .getAllByRole("cell")
      .map((cell) => cell.textContent);

    expect(cells[0]).toBe("Acme_MSA.pdf");
    expect(cells[1]).toBe("Order Form");
    expect(cells[3]).toBe("Needs review");
    expect(cells[4]).toBe("06/09/2026, 08:05");
  });

  it("marks the Supplier column as not yet available -- Document carries no supplier field today (AC-1)", () => {
    renderTable([trackedDocument()]);

    expect(screen.getByText("Not yet available")).toBeInTheDocument();
  });

  it.each<{ processingStatus: "NeedsReview" | "Completed" | "Failed"; tagClass: string; tagText: string }>([
    { processingStatus: "Completed", tagClass: "tag-neutral", tagText: "Completed" },
    { processingStatus: "NeedsReview", tagClass: "tag-outline", tagText: "Needs review" },
    { processingStatus: "Failed", tagClass: "tag-accent", tagText: "Failed" },
  ])("renders the $processingStatus status as its ADR-019 tag (AC-2)", ({ processingStatus, tagClass, tagText }) => {
    renderTable([trackedDocument({ processingStatus })]);

    const tag = screen.getByText(tagText);
    expect(tag).toHaveClass("tag", tagClass);
  });

  it("links a row with a contractId to Contract 360 (AC-3)", () => {
    renderTable([trackedDocument({ contractId: "contract-42" })]);

    const link = screen.getByRole("link", { name: "Acme_MSA.pdf" });
    expect(link).toHaveAttribute("href", "/contracts/contract-42");
  });

  it("renders plain text with a visible reason instead of a dead link when contractId is null (AC-3)", () => {
    renderTable([trackedDocument({ contractId: null, processingStatus: "Failed" })]);

    expect(screen.queryByRole("link", { name: "Acme_MSA.pdf" })).not.toBeInTheDocument();
    expect(screen.getByText("Acme_MSA.pdf")).toBeInTheDocument();
    expect(screen.getByText("Not yet linked to a contract")).toBeInTheDocument();
  });

  it("shows 'Classifying…' for the Type column while documentType has not been read back yet", () => {
    renderTable([trackedDocument({ documentType: null })]);

    expect(screen.getByText("Classifying…")).toBeInTheDocument();
  });

  it("renders multiple documents as separate rows, most-recently-tracked first", () => {
    renderTable([
      trackedDocument({ id: "doc-2", fileName: "Second.pdf" }),
      trackedDocument({ id: "doc-1", fileName: "First.pdf" }),
    ]);

    const rows = screen.getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell")[0].textContent)).toEqual(["Second.pdf", "First.pdf"]);
  });
});
