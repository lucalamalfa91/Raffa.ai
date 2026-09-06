import { describe, expect, it } from "vitest";
import { formatUploadedAt, getDocumentStatusTag, getDocumentTypeLabel } from "../../../src/routes/documents/documentTable";

// Task-01's own named "Tests required" row: "unit | status→tag semantic
// mapping". AC-2: "Status tags honour ADR-019 semantic mapping
// (completed/needs_review/failed)".
describe("getDocumentStatusTag (AC-2: status→tag semantic mapping, ADR-019)", () => {
  it.each<{
    processingStatus: "NeedsReview" | "Completed" | "Failed";
    variant: "neutral" | "accent" | "outline";
    label: string;
  }>([
    { processingStatus: "Completed", variant: "neutral", label: "Completed" },
    { processingStatus: "NeedsReview", variant: "outline", label: "Needs review" },
    { processingStatus: "Failed", variant: "accent", label: "Failed" },
  ])("maps $processingStatus to a $variant tag", ({ processingStatus, variant, label }) => {
    expect(getDocumentStatusTag(processingStatus)).toEqual({ variant, label });
  });
});

describe("getDocumentTypeLabel", () => {
  it("renders a 'Classifying…' loading state while documentType is not yet read back", () => {
    expect(getDocumentTypeLabel(null)).toBe("Classifying…");
  });

  // Labels are quoted verbatim from ContractDocumentType.cs's own doc
  // comment (spec §6.1: "MSA / Order Form / Amendment / SOW / Renewal
  // Letter"), not re-derived from the wire enum's PascalCase member names.
  it.each<{ documentType: "Msa" | "OrderForm" | "Amendment" | "Sow" | "RenewalLetter" | "Other"; label: string }>([
    { documentType: "Msa", label: "MSA" },
    { documentType: "OrderForm", label: "Order Form" },
    { documentType: "Amendment", label: "Amendment" },
    { documentType: "Sow", label: "SOW" },
    { documentType: "RenewalLetter", label: "Renewal Letter" },
    { documentType: "Other", label: "Other" },
  ])("maps $documentType to '$label'", ({ documentType, label }) => {
    expect(getDocumentTypeLabel(documentType)).toBe(label);
  });
});

describe("formatUploadedAt", () => {
  it("formats a fixed UTC date/time regardless of the host timezone/locale", () => {
    expect(formatUploadedAt("2026-09-06T08:05:00Z")).toBe("06/09/2026, 08:05");
  });

  it("does not shift across a UTC day boundary (guards against a local-timezone formatter)", () => {
    expect(formatUploadedAt("2026-01-01T23:30:00Z")).toBe("01/01/2026, 23:30");
  });
});
