import { describe, expect, it } from "vitest";
import {
  formatAnnualSpend,
  formatAutoRenewal,
  formatDateOnly,
  formatSupplier,
  getContractTypeLabel,
  getPortfolioRiskTag,
  getPortfolioStatusTag,
} from "../../../src/routes/contracts/portfolioTableFormatters";
import type { PortfolioContractType } from "../../../src/api/client";

describe("getContractTypeLabel", () => {
  it.each<{ type: PortfolioContractType; label: string }>([
    { type: "Msa", label: "MSA" },
    { type: "OrderForm", label: "Order Form" },
    { type: "Amendment", label: "Amendment" },
    { type: "Sow", label: "SOW" },
    { type: "RenewalLetter", label: "Renewal Letter" },
    { type: "Other", label: "Other" },
  ])("maps $type to '$label'", ({ type, label }) => {
    expect(getContractTypeLabel(type)).toBe(label);
  });
});

describe("formatDateOnly", () => {
  it("renders a DateOnly wire value as DD/MM/YYYY", () => {
    expect(formatDateOnly("2026-03-05")).toBe("05/03/2026");
  });

  it("renders a null date as an em dash", () => {
    expect(formatDateOnly(null)).toBe("—");
  });
});

describe("formatAnnualSpend", () => {
  it("groups a large number without inventing a currency symbol (no currency in this wire response)", () => {
    expect(formatAnnualSpend(1_234_567)).toBe("1,234,567");
  });

  it("renders a null spend as an em dash, not zero", () => {
    expect(formatAnnualSpend(null)).toBe("—");
  });
});

describe("formatAutoRenewal", () => {
  it("renders Yes/No, never a bare boolean", () => {
    expect(formatAutoRenewal(true)).toBe("Yes");
    expect(formatAutoRenewal(false)).toBe("No");
  });
});

describe("formatSupplier", () => {
  it("shows a short id fragment with the full id as the tooltip title", () => {
    const result = formatSupplier("22222222-2222-2222-2222-222222222222");
    expect(result.label).toBe("Supplier 22222222");
    expect(result.title).toBe("22222222-2222-2222-2222-222222222222");
  });

  it("renders a null supplier as an honest em dash, not a fabricated id", () => {
    expect(formatSupplier(null)).toEqual({ label: "—", title: undefined });
  });
});

describe("getPortfolioStatusTag", () => {
  it("tags an exact 'failed' as accent", () => {
    expect(getPortfolioStatusTag("failed")).toEqual({ variant: "accent", label: "Failed" });
  });

  it("tags the bootstrap 'processing' status as outline", () => {
    expect(getPortfolioStatusTag("processing")).toEqual({ variant: "outline", label: "Processing" });
  });

  it("tags any status containing 'review' as outline", () => {
    expect(getPortfolioStatusTag("Needs Review")).toEqual({ variant: "outline", label: "Needs review" });
  });

  it("tags any other free-text business status as neutral, capitalized", () => {
    expect(getPortfolioStatusTag("active")).toEqual({ variant: "neutral", label: "Active" });
    expect(getPortfolioStatusTag("Expired")).toEqual({ variant: "neutral", label: "Expired" });
  });

  it("is case-insensitive for the closed-vocabulary checks", () => {
    expect(getPortfolioStatusTag("FAILED")).toEqual({ variant: "accent", label: "Failed" });
    expect(getPortfolioStatusTag("PROCESSING")).toEqual({ variant: "outline", label: "Processing" });
  });
});

describe("getPortfolioRiskTag", () => {
  it("tags High as accent (ADR-019 locked mapping)", () => {
    expect(getPortfolioRiskTag("High")).toEqual({ variant: "accent", label: "High risk" });
  });

  it("folds Critical into the same accent 'High risk' treatment (ADR-019 defines no fourth tier)", () => {
    expect(getPortfolioRiskTag("Critical")).toEqual({ variant: "accent", label: "High risk" });
  });

  it("tags Medium and Low as neutral", () => {
    expect(getPortfolioRiskTag("Medium")).toEqual({ variant: "neutral", label: "Medium risk" });
    expect(getPortfolioRiskTag("Low")).toEqual({ variant: "neutral", label: "Low risk" });
  });

  it("gives null (no recorded risk) its own honest label, never a fabricated 'Low risk'", () => {
    expect(getPortfolioRiskTag(null)).toEqual({ variant: "neutral", label: "No risk recorded" });
  });
});
