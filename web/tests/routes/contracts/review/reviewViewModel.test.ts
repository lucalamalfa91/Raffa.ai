import { describe, expect, it } from "vitest";
import type { Contract360Body, CorrectionHistoryEntryBody } from "../../../../src/api/client";
import {
  blockedReason,
  buildReviewFields,
  computeReviewProgress,
  fieldTag,
  formatCorrectableValue,
  isFieldBlocking,
  isValidationBlocked,
  readCorrectableValue,
  type ReviewFieldRow,
} from "../../../../src/routes/contracts/review/reviewViewModel";

const CONTRACT_ID = "11111111-1111-1111-1111-111111111111";

function contract(overrides: Partial<Contract360Body> = {}): Contract360Body {
  return {
    contractId: CONTRACT_ID,
    header: {
      contractId: CONTRACT_ID,
      supplierId: null,
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
