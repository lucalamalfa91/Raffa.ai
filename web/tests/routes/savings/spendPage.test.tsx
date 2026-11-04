import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { SpendReportQuery } from "../../../src/api/spend";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { trackMissingItalian } from "../../../src/i18n/translate";
import SavingsRoute from "../../../src/routes/savings";
import { spendReport } from "./spendFixture";

const TENANT = "11111111-1111-1111-1111-111111111111";

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.search}</output>;
}

function renderPage(api: Partial<ApiClient>, at = "/savings") {
  render(
    <MemoryRouter initialEntries={[at]}>
      <I18nProvider>
        <Routes>
          <Route
            path="/savings"
            element={
              <>
                <SavingsRoute apiClient={api as ApiClient} />
                <Where />
              </>
            }
          />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

const ok = <T,>(data: T) => ({ ok: true, statusCode: 200, data, error: null });

const missing: string[] = [];
beforeEach(() => {
  window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: TENANT, name: "Helvetia Foods AG" }));
  window.localStorage.setItem("raffa4.lang", "it");
  missing.length = 0;
  trackMissingItalian((id) => missing.push(id));
});
afterEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
  trackMissingItalian(null);
  vi.restoreAllMocks();
});

describe("Spesa e risparmi", () => {
  it("shows spend and savings of the fiscal year, what is left out and why, in Italian", async () => {
    const getSpendReport = vi.fn().mockResolvedValue(ok(spendReport()));
    renderPage({ getSpendReport, getActiveSavingsGoal: vi.fn().mockResolvedValue(ok(null)) });

    expect(await screen.findByRole("heading", { name: "Spesa e risparmi" })).toBeTruthy();
    expect(getSpendReport).toHaveBeenCalledWith(TENANT, { basis: "accrual" });
    expect(screen.getByText("Spesa FY 2026 · competenza")).toBeTruthy();
    expect(screen.getAllByText("CHF 190k").length).toBeGreaterThan(0);
    expect(screen.getByText("Risparmi firmati FY 2026")).toBeTruthy();

    const note = screen.getByRole("note");
    expect(note.textContent).toContain("1 senza cambio di budget (GBP)");
    expect(within(note).getByRole("link", { name: "imposta i cambi in Impostazioni" }).getAttribute("href")).toBe("/settings");
    expect(missing).toEqual([]);
  });

  it("keeps the filters in the URL and refetches when the basis changes", async () => {
    const getSpendReport = vi.fn().mockImplementation((_t: string, q?: SpendReportQuery) => Promise.resolve(ok({ ...spendReport(), basis: q?.basis ?? "accrual" })));
    renderPage({ getSpendReport, getActiveSavingsGoal: vi.fn().mockResolvedValue(ok(null)) });
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Trimestri" }));
    expect(screen.getByTestId("where").textContent).toBe("?view=quarter");
    expect(screen.getByText("Q4 2026 · oggi")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Cassa" }));
    expect(screen.getByTestId("where").textContent).toBe("?view=quarter&basis=cash");
    expect(getSpendReport).toHaveBeenLastCalledWith(TENANT, { basis: "cash" });
    expect(missing).toEqual([]);
  });

  it("opens a contract's incidence and savings, and exports the table", async () => {
    const createObjectURL = vi.fn().mockReturnValue("blob:spend");
    Object.defineProperty(URL, "createObjectURL", { value: createObjectURL, configurable: true });
    Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
    renderPage({ getSpendReport: vi.fn().mockResolvedValue(ok(spendReport())), getActiveSavingsGoal: vi.fn().mockResolvedValue(ok(null)) });
    const user = userEvent.setup();

    await screen.findByRole("heading", { name: "Contratti e incidenza · tutti gli anni" });
    const table = screen.getAllByRole("table")[0]!;
    await user.click(within(table).getByRole("button", { name: /Zscaler/ }));
    const detail = screen.getByRole("complementary", { name: "Incidenza e risparmi del contratto" });
    expect(within(detail).getByRole("heading", { name: "Zscaler" })).toBeTruthy();
    expect(within(detail).getByText("EUR, al cambio di budget 0.9")).toBeTruthy();
    expect(within(detail).getByText("Provato da 1 fattura fino al 30 Apr 2026")).toBeTruthy();
    expect(screen.getByTestId("where").textContent).toBe("?contract=zscaler");

    await user.click(screen.getByRole("button", { name: "Esporta CSV" }));
    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect(missing).toEqual([]);
  });

  it("lists the contracts to act on next year with the case objective", async () => {
    renderPage({ getSpendReport: vi.fn().mockResolvedValue(ok(spendReport())), getActiveSavingsGoal: vi.fn().mockResolvedValue(ok(null)) });

    expect(await screen.findByRole("heading", { name: "Budget FY 2027 dai contratti" })).toBeTruthy();
    const actions = screen.getAllByRole("table")[1]!;
    const rows = within(actions).getAllByRole("row").slice(1);
    expect(within(rows[0]!).getByText("Coupa")).toBeTruthy();
    expect(within(rows[0]!).getByRole("button", { name: "Apri la pratica" })).toBeTruthy();
    expect(within(rows[1]!).getByRole("button", { name: "Avvia e fissa un obiettivo" })).toBeTruthy();
    expect(missing).toEqual([]);
  });
});
