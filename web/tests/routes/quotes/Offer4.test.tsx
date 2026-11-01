import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { readOpenCases, registerAskOfferTransport, type ApiClient, type QuoteView4 } from "../../../src/api/client";
import QuoteCheckRoute from "../../../src/routes/quotes";

const VIEW: QuoteView4 = {
  id: "q1",
  reference: "QC-07",
  supplier: "Databricks",
  fileName: "Databricks_Proposal_Q-88213.pdf",
  kind: "new",
  status: "checked",
  term: "12 months",
  valid: "30 Sep 2026",
  validUntil: "2026-09-30",
  quoteDate: "2026-09-02",
  checkedBy: "Marta Keller",
  decidedBy: null,
  decidedAt: null,
  contractId: null,
  caseId: null,
  extractionConfirmedAt: null,
  lines: [
    { name: "Enterprise Support", sku: "S", qty: 1, unit: "year", price: 50000, annual: 50000, existing: null, existingLabel: null, delta: null, qtyLabel: "1 year", priceLabel: "CHF 50,000", annualLabel: "CHF 50k" },
  ],
  stated: [{ key: "payment", value: "Annual in advance" }, { key: "uplift", value: null }],
  assessment: {
    verdict: "nocompare", basis: "none", total: 50000, totalLabel: "CHF 50k", curTotal: null, curTotalLabel: null, own: null, ownLabel: null,
    partial: false, target: null, targetTotal: null, targetMet: null,
    points: [{ kind: "gap", tone: "alert", text: "Not stated: Annual uplift not stated", src: "the quote itself" }],
    missing: ["your current contract price for these lines"], gaps: [], evaluable: [],
  },
};

function renderOffer() {
  const client = {} as ApiClient;
  const calls: string[] = [];
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push(`${init?.method ?? "GET"} ${String(input)}`);
    const body = String(input).includes("/decision") ? { ...VIEW, status: "signing" } : VIEW;
    return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
  });
  registerAskOfferTransport(client, { baseUrl: "http://api.test", getAccessToken: async () => null, fetch });
  render(
    <MemoryRouter initialEntries={["/quotes/q1"]}>
      <Routes>
        <Route path="/quotes/:quoteId" element={<QuoteCheckRoute apiClient={client} />} />
      </Routes>
    </MemoryRouter>,
  );
  return calls;
}

describe("Offerta (quote check without benchmark)", () => {
  it("walks Verify → Compare → Decide with the omitted terms and no benchmark", async () => {
    const calls = renderOffer();
    expect(await screen.findByText("Check what Raffa read")).toBeTruthy();
    expect(screen.getByText("not stated")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Confirm the extracted data" }));
    expect(await screen.findByText("No market benchmark in this release.")).toBeTruthy();
    expect(screen.getByText("the quote itself")).toBeTruthy();
    expect(calls.some((c) => c.startsWith("POST") && c.includes("/confirm-extraction"))).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Go to the decision" }));
    expect(await screen.findByText("No price comparison available.")).toBeTruthy();
    expect(screen.getAllByRole("button").filter((b) => b.className.includes("q6-choice"))).toHaveLength(3);
    fireEvent.click(screen.getByRole("button", { name: "Proceed towards signature" }));
    expect(await screen.findByText("Reopen the decision")).toBeTruthy();
  });
});

describe("readOpenCases", () => {
  it("keeps open cases from any tolerated shape and nothing else", () => {
    expect(readOpenCases({ items: [{ caseId: "c1", supplier: "Salesforce", contractName: "Sales Cloud — MSA", phaseLabel: "Start" }, { caseId: "c2", supplier: "X", outcome: "renewed" }] }))
      .toEqual([{ caseId: "c1", supplier: "Salesforce", contract: "Sales Cloud — MSA", phase: "Start" }]);
    expect(readOpenCases({ legacy: true })).toEqual([]);
    expect(readOpenCases(null)).toEqual([]);
  });
});
