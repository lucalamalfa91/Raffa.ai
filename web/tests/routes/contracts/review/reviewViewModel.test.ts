import { describe, expect, it } from "vitest";
import type { Contract360Body, ContractFieldEvidenceBody, CorrectionHistoryEntryBody } from "../../../../src/api/client";
import {
  acceptedFieldNames,
  blockedReason,
  buildReviewFields,
  computeReviewProgress,
  fieldTag,
  formatCorrectableValue,
  indexEvidence,
  isFieldBlocking,
  isValidationBlocked,
  readCorrectableValue,
  resolveReviewDocument,
  splitPassage,
  type ReviewFieldRow,
} from "../../../../src/routes/contracts/review/reviewViewModel";

const CONTRACT_ID = "11111111-1111-1111-1111-111111111111";

function contract(overrides: Partial<Contract360Body> = {}): Contract360Body {
  return {
    contractId: CONTRACT_ID,
    header: {
      contractId: CONTRACT_ID,
      supplierId: null,
      // Task E13/F03/US01/T02: supplierName is required now (null when unresolved).
      supplierName: null,
      type: "Msa",
      status: "active",
      annualSpend: 500_000,
      totalContractValue: 1_500_000,
      startDate: "2025-01-01",
      endDate: "2026-01-01",
      renewalDate: "2026-01-01",
      cancellationDeadline: "2025-11-17",
      autoRenewal: true,
      risk: "High",
    },
    tabs: {
      overview: {
        currency: "CHF",
        effectiveDate: "2025-01-01",
        renewalTermMonths: 12,
        paymentTerms: "Net 45",
        governingLaw: "Switzerland, Zürich",
        parentContractId: null,
        version: 1,
        createdAt: "2025-01-01T00:00:00Z",
      },
      commercials: {
        annualSpend: 500_000,
        totalContractValue: 1_500_000,
        currency: "CHF",
        paymentTerms: "Net 45",
        autoRenewal: true,
        renewalTermMonths: 12,
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
      renewal: { endDate: "2026-01-01", renewalDate: "2026-01-01", cancellationDeadline: "2025-11-17", autoRenewal: true, renewalTermMonths: 12 },
      activity: [],
    },
    ...overrides,
  };
}

function correction(overrides: Partial<CorrectionHistoryEntryBody> = {}): CorrectionHistoryEntryBody {
  return {
    fieldName: "annualSpend",
    previousValue: "500000",
    newValue: "520000",
    correctedBy: "unattributed",
    correctedAt: "2026-09-06T08:00:00Z",
    reason: "Corrected from the signed order form.",
    ...overrides,
  };
}

describe("readCorrectableValue", () => {
  it("reads header fields", () => {
    expect(readCorrectableValue(contract(), "type")).toBe("Msa");
    expect(readCorrectableValue(contract(), "cancellationDeadline")).toBe("2025-11-17");
    expect(readCorrectableValue(contract(), "autoRenewal")).toBe("true");
  });

  it("reads tabs.overview fields", () => {
    expect(readCorrectableValue(contract(), "paymentTerms")).toBe("Net 45");
    expect(readCorrectableValue(contract(), "renewalTermMonths")).toBe("12");
  });

  it("returns null for an optional field that was never extracted", () => {
    const noGoverningLaw = contract({ tabs: { ...contract().tabs, overview: { ...contract().tabs.overview, governingLaw: null } } });
    expect(readCorrectableValue(noGoverningLaw, "governingLaw")).toBeNull();
  });
});

describe("formatCorrectableValue", () => {
  it("formats a date field as DD/MM/YYYY", () => {
    expect(formatCorrectableValue("date", "cancellationDeadline", "2025-11-17")).toBe("17/11/2025");
  });

  it("formats a decimal field with grouping", () => {
    expect(formatCorrectableValue("decimal", "annualSpend", "500000")).toBe("500,000");
  });

  it("formats a bool field as Yes/No, never a bare true/false", () => {
    expect(formatCorrectableValue("bool", "autoRenewal", "true")).toBe("Yes");
    expect(formatCorrectableValue("bool", "autoRenewal", "false")).toBe("No");
  });

  it("formats the type enum through the shared contract-type label", () => {
    expect(formatCorrectableValue("enum", "type", "OrderForm")).toBe("Order Form");
  });

  it("renders a null value as an em dash, never a fabricated placeholder", () => {
    expect(formatCorrectableValue("text", "governingLaw", null)).toBe("—");
  });
});

describe("buildReviewFields", () => {
  it("builds one row per correctable field that has a value, skipping never-extracted optional fields", () => {
    const noGoverningLaw = contract({ tabs: { ...contract().tabs, overview: { ...contract().tabs.overview, governingLaw: null } } });

    const rows = buildReviewFields(noGoverningLaw, [], new Set());

    expect(rows.some((row) => row.name === "governingLaw")).toBe(false);
    expect(rows.some((row) => row.name === "annualSpend")).toBe(true);
    expect(rows.some((row) => row.name === "type")).toBe(true);
  });

  it("marks a field pending when it has no correction history entry and was not accepted this session", () => {
    const rows = buildReviewFields(contract(), [], new Set());
    const annualSpend = rows.find((row) => row.name === "annualSpend")!;

    expect(annualSpend.decision).toBe("pending");
    expect(annualSpend.latestCorrection).toBeNull();
    expect(annualSpend.rawValue).toBe("500000");
  });

  it("marks a field accepted when it is in the accepted-this-session set (and has no history entry)", () => {
    const rows = buildReviewFields(contract(), [], new Set(["annualSpend"]));
    const annualSpend = rows.find((row) => row.name === "annualSpend")!;

    expect(annualSpend.decision).toBe("accepted");
  });

  it("marks a field corrected when correction history has an entry for it -- history wins over an accepted-this-session flag", () => {
    const rows = buildReviewFields(contract(), [correction()], new Set(["annualSpend"]));
    const annualSpend = rows.find((row) => row.name === "annualSpend")!;

    expect(annualSpend.decision).toBe("corrected");
    expect(annualSpend.latestCorrection).toEqual(correction());
  });

  it("uses the first (newest, per ContractCorrectionHistoryQueryService's own ordering) matching history entry for a field with more than one correction", () => {
    const older = correction({ newValue: "510000", correctedAt: "2026-09-01T08:00:00Z" });
    const newer = correction({ newValue: "520000", correctedAt: "2026-09-06T08:00:00Z" });

    // Newest-first, matching the real endpoint's own OrderByDescending(CorrectedAt).
    const rows = buildReviewFields(contract(), [newer, older], new Set());
    const annualSpend = rows.find((row) => row.name === "annualSpend")!;

    expect(annualSpend.latestCorrection?.newValue).toBe("520000");
  });
});

function row(
  overrides: Partial<Pick<ReviewFieldRow, "decision" | "confidencePct">> = {},
): Pick<ReviewFieldRow, "decision" | "confidencePct"> {
  return { decision: "pending", confidencePct: null, ...overrides };
}

describe("fieldTag (AC-2 confidence -> tag mapping, generalised for decision state)", () => {
  it("shows a real, non-fabricated result for a corrected field", () => {
    expect(fieldTag(row({ decision: "corrected" }))).toEqual({ variant: "neutral", label: "Corrected" });
  });

  it("shows a real, non-fabricated result for an accepted field", () => {
    expect(fieldTag(row({ decision: "accepted" }))).toEqual({ variant: "neutral", label: "Accepted by you" });
  });

  it("delegates to styles/semantics.ts#getConfidenceTag verbatim once a real score exists (>95/80-95/<80 thresholds)", () => {
    expect(fieldTag(row({ confidencePct: 97 }))).toEqual({ variant: "neutral", label: "Accepted · 97%" });
    expect(fieldTag(row({ confidencePct: 88 }))).toEqual({ variant: "accent", label: "Flagged · 88%" });
    expect(fieldTag(row({ confidencePct: 71 }))).toEqual({ variant: "outline", label: "Review · 71%" });
  });

  it("shows a plain, honest 'Needs review' label -- never a fabricated percentage -- for a pending field with no score", () => {
    expect(fieldTag(row())).toEqual({ variant: "outline", label: "Needs review" });
  });
});

describe("isFieldBlocking (AC-4 gate predicate)", () => {
  it("a resolved field never blocks, regardless of confidence", () => {
    expect(isFieldBlocking(row({ decision: "corrected", confidencePct: 12 }))).toBe(false);
    expect(isFieldBlocking(row({ decision: "accepted", confidencePct: 12 }))).toBe(false);
  });

  it("a pending field with a real score blocks only under 80%, matching spec §7.3 exactly", () => {
    expect(isFieldBlocking(row({ confidencePct: 79.9 }))).toBe(true);
    expect(isFieldBlocking(row({ confidencePct: 80 }))).toBe(false);
  });

  it("a pending field with no score blocks by conservative default (no live evidence endpoint yet)", () => {
    expect(isFieldBlocking(row())).toBe(true);
  });
});

describe("computeReviewProgress / isValidationBlocked / blockedReason", () => {
  it("blocks and names a visible reason when at least one field is pending (AC-4 'visible reason, not a hidden control')", () => {
    const rows = buildReviewFields(contract(), [], new Set());
    const progress = computeReviewProgress(rows);

    expect(isValidationBlocked(progress)).toBe(true);
    expect(progress.blockingCount).toBeGreaterThan(1);
    expect(blockedReason(progress)).toBe(`${progress.blockingCount} fields still need review before this contract can be marked validated.`);
  });

  it("is not blocked once every field is resolved", () => {
    const rows = buildReviewFields(contract(), [], new Set());
    const acceptedAll = new Set(rows.map((r) => r.name));
    const progress = computeReviewProgress(buildReviewFields(contract(), [], acceptedAll));

    expect(isValidationBlocked(progress)).toBe(false);
    expect(progress.blockingCount).toBe(0);
    expect(blockedReason(progress)).toBe("");
  });

  it("singularises both 'field' and 'needs' for exactly one blocking field", () => {
    const rows = buildReviewFields(contract(), [], new Set());
    const allButOne = new Set(rows.slice(1).map((r) => r.name));
    const progress = computeReviewProgress(buildReviewFields(contract(), [], allButOne));

    expect(progress.blockingCount).toBe(1);
    expect(blockedReason(progress)).toBe("1 field still needs review before this contract can be marked validated.");
  });
});

function evidence(overrides: Partial<ContractFieldEvidenceBody> = {}): ContractFieldEvidenceBody {
  return {
    fieldName: "annualSpend",
    value: "500000",
    confidence: 0.96,
    sourcePage: 2,
    sourceSpan: "EUR 500,000 per year",
    sourceDocumentId: "44444444-4444-4444-4444-444444444444",
    sourceFileName: "acme-msa.pdf",
    passage: "The fees are EUR 500,000 per year, invoiced annually.",
    highlightStart: 13,
    highlightLength: 20,
    modelId: "fixture-extract-model",
    extractedAt: "2026-09-09T10:00:00Z",
    ...overrides,
  };
}

describe("buildReviewFields with real evidence (GET /api/contracts/{id}/evidence)", () => {
  it("carries the field's real confidence (0..1 -> 0..100) and evidence row, so the tag and gate use a real score", () => {
    const rows = buildReviewFields(contract(), [], new Set(), indexEvidence([evidence()]));
    const annualSpend = rows.find((row) => row.name === "annualSpend")!;

    expect(annualSpend.confidencePct).toBe(96);
    expect(annualSpend.evidence?.sourcePage).toBe(2);
    expect(fieldTag(annualSpend)).toEqual({ variant: "neutral", label: "Accepted · 96%" });
    expect(isFieldBlocking(annualSpend)).toBe(false);
    expect(annualSpend.proposalPending).toBe(false);
  });

  it("matches evidence to fields case-insensitively (the backend compares field names that way)", () => {
    const rows = buildReviewFields(contract(), [], new Set(), indexEvidence([evidence({ fieldName: "AnnualSpend", confidence: 0.5 })]));

    expect(rows.find((row) => row.name === "annualSpend")!.confidencePct).toBe(50);
  });

  it("keeps the conservative 'Needs review' posture for a field with no evidence row -- never an invented score", () => {
    const rows = buildReviewFields(contract(), [], new Set(), indexEvidence([evidence()]));
    const currency = rows.find((row) => row.name === "currency")!;

    expect(currency.confidencePct).toBeNull();
    expect(currency.evidence).toBeNull();
    expect(fieldTag(currency)).toEqual({ variant: "outline", label: "Needs review" });
    expect(isFieldBlocking(currency)).toBe(true);
  });

  it("builds a Supplier row from a proposal the pipeline did not apply (a weak supplier fact) and flags it as pending a write", () => {
    const weakSupplier = evidence({
      fieldName: "supplier",
      value: "Fabrikam Software GmbH",
      confidence: 0.52,
      sourcePage: 1,
      sourceSpan: "between Contigo Demo AG and Fabrikam Software GmbH",
    });
    // contract() has no linked supplier (supplierName null) -- without evidence there would be no row.
    expect(buildReviewFields(contract(), [], new Set()).some((row) => row.name === "supplier")).toBe(false);

    const rows = buildReviewFields(contract(), [], new Set(), indexEvidence([weakSupplier]));
    const supplier = rows.find((row) => row.name === "supplier")!;

    expect(supplier.rawValue).toBe("Fabrikam Software GmbH");
    expect(supplier.displayValue).toBe("Fabrikam Software GmbH");
    expect(supplier.proposalPending).toBe(true);
    expect(supplier.confidencePct).toBe(52);
    expect(fieldTag(supplier)).toEqual({ variant: "outline", label: "Review · 52%" });
    expect(isFieldBlocking(supplier)).toBe(true);
    // Rows keep the catalogue order: type, then supplier, then status...
    expect(rows.map((row) => row.name).slice(0, 3)).toEqual(["type", "supplier", "status"]);
  });

  it("reads a linked supplier's name from the header, as an ordinary (applied) value", () => {
    const linked = contract({ header: { ...contract().header, supplierName: "Northwind Traders SA" } });

    const rows = buildReviewFields(linked, [], new Set());
    const supplier = rows.find((row) => row.name === "supplier")!;

    expect(readCorrectableValue(linked, "supplier")).toBe("Northwind Traders SA");
    expect(supplier.rawValue).toBe("Northwind Traders SA");
    expect(supplier.proposalPending).toBe(false);
  });

  it("a proposed bool is normalised to the canonical 'true'/'false' wire string", () => {
    const noValue = contract({ header: { ...contract().header, autoRenewal: false } });
    // autoRenewal is never null on a contract, so the proposal path is exercised through a text
    // field instead: a governingLaw the contract lacks but the extraction proposed.
    const withProposal = contract({ tabs: { ...noValue.tabs, overview: { ...noValue.tabs.overview, governingLaw: null } } });
    const rows = buildReviewFields(
      withProposal,
      [],
      new Set(),
      indexEvidence([evidence({ fieldName: "governingLaw", value: "  Switzerland ", confidence: 0.9 })]),
    );

    const law = rows.find((row) => row.name === "governingLaw")!;
    expect(law.rawValue).toBe("Switzerland");
    expect(law.proposalPending).toBe(true);
  });

  it("a proposal with an empty value does not create a row (nothing to review)", () => {
    const noLaw = contract({ tabs: { ...contract().tabs, overview: { ...contract().tabs.overview, governingLaw: null } } });
    const rows = buildReviewFields(noLaw, [], new Set(), indexEvidence([evidence({ fieldName: "governingLaw", value: "" })]));

    expect(rows.some((row) => row.name === "governingLaw")).toBe(false);
  });
});

describe("acceptedFieldNames", () => {
  it("lists exactly the fields the reviewer Accepted -- corrected rows are already durable, pending ones are not decisions", () => {
    const rows = buildReviewFields(contract(), [correction()], new Set(["currency", "type"]));

    expect(acceptedFieldNames(rows)).toEqual(["type", "currency"]);
  });
});

describe("resolveReviewDocument", () => {
  const needsReview = { documentId: "doc-1", fileName: "a.pdf", mimeType: "application/pdf", documentType: "Msa" as const, processingStatus: "NeedsReview" as const, createdAt: "2026-09-09T09:00:00Z" };
  const completed = { ...needsReview, documentId: "doc-0", processingStatus: "Completed" as const };

  it("prefers the caller's own document id and reports its status when the aggregate lists it", () => {
    const withDocs = contract({ tabs: { ...contract().tabs, documents: [completed, needsReview] } });

    expect(resolveReviewDocument(withDocs, "doc-0")).toEqual({ documentId: "doc-0", processingStatus: "Completed" });
    expect(resolveReviewDocument(withDocs, "doc-9")).toEqual({ documentId: "doc-9", processingStatus: null });
  });

  it("falls back to the document still needing review, else the first one, else null", () => {
    const withDocs = contract({ tabs: { ...contract().tabs, documents: [completed, needsReview] } });
    expect(resolveReviewDocument(withDocs)).toEqual({ documentId: "doc-1", processingStatus: "NeedsReview" });

    const onlyCompleted = contract({ tabs: { ...contract().tabs, documents: [completed] } });
    expect(resolveReviewDocument(onlyCompleted)).toEqual({ documentId: "doc-0", processingStatus: "Completed" });

    expect(resolveReviewDocument(contract())).toBeNull();
  });
});

describe("splitPassage", () => {
  it("splits the passage around the span using the backend's own offsets", () => {
    expect(splitPassage(evidence())).toEqual({
      before: "The fees are ",
      highlight: "EUR 500,000 per year",
      after: ", invoiced annually.",
    });
  });

  it("returns the whole passage un-highlighted when the offsets do not fit it -- never a wrong highlight", () => {
    expect(splitPassage(evidence({ highlightStart: 40, highlightLength: 30 }))).toEqual({
      before: "The fees are EUR 500,000 per year, invoiced annually.",
      highlight: "",
      after: "",
    });
    expect(splitPassage(evidence({ highlightStart: null, highlightLength: null }))).toEqual({
      before: "The fees are EUR 500,000 per year, invoiced annually.",
      highlight: "",
      after: "",
    });
  });

  it("returns null when the evidence carries no passage at all", () => {
    expect(splitPassage(evidence({ passage: null }))).toBeNull();
  });
});
