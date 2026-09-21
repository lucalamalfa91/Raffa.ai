import { describe, expect, it } from "vitest";
import {
  formatAnnualSpend,
  formatAutoRenewal,
  formatDateOnly,
  formatPortfolioDate,
  formatRisk,
  formatSupplier,
  getContractTypeLabel,
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
  it("renders a DateOnly wire value as DD/MM/YYYY (Contract 360 / Review)", () => {
    expect(formatDateOnly("2026-03-05")).toBe("05/03/2026");
  });

  it("renders a null date as an em dash", () => {
    expect(formatDateOnly(null)).toBe("—");
  });
});

describe("formatPortfolioDate", () => {
  it("renders a DateOnly wire value as DD Mon YYYY, the prototype's own fixture format ('02 Oct 2026')", () => {
    expect(formatPortfolioDate("2026-03-05")).toBe("05 Mar 2026");
    expect(formatPortfolioDate("2026-10-02")).toBe("02 Oct 2026");
    expect(formatPortfolioDate("2027-12-31")).toBe("31 Dec 2027");
  });

  it("falls back to the wire value rather than a fabricated month when the month is out of range", () => {
    expect(formatPortfolioDate("2026-13-05")).toBe("2026-13-05");
  });

  it("renders a null date as an em dash", () => {
    expect(formatPortfolioDate(null)).toBe("—");
  });
});

describe("formatAnnualSpend", () => {
  it("renders the prototype's compact figure (`c.spendFmt`): 'CHF 58k', 'CHF 1.2M'", () => {
    expect(formatAnnualSpend(58_000, "CHF")).toBe("CHF 58k");
    expect(formatAnnualSpend(1_234_567, "CHF")).toBe("CHF 1.2M");
  });

  it("never invents a currency code when the row carried none", () => {
    expect(formatAnnualSpend(640_000, null)).toBe("640k");
    expect(formatAnnualSpend(640_000, undefined)).toBe("640k");
  });

  it("renders a null spend as an em dash, not zero", () => {
    expect(formatAnnualSpend(null, "CHF")).toBe("—");
  });
});

describe("formatRisk", () => {
  it("renders the recorded severity as plain text (`{{ c.risk }}`), never a tag label", () => {
    expect(formatRisk("High")).toBe("High");
    expect(formatRisk("Medium")).toBe("Medium");
    expect(formatRisk("Low")).toBe("Low");
    expect(formatRisk("Critical")).toBe("Critical");
  });

  it("renders null (no recorded risk) as an honest em dash, never a fabricated 'Low'", () => {
    expect(formatRisk(null)).toBe("—");
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
