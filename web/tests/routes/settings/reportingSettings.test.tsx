import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import type { ApiClient, GetWorkspaceSettingsResult, WorkspaceSettingsBody } from "../../../src/api/client";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { trackMissingItalian } from "../../../src/i18n/translate";
import ReportingSettings from "../../../src/routes/settings/ReportingSettings";

const TENANT = "11111111-1111-1111-1111-111111111111";

const body = (over: Partial<WorkspaceSettingsBody> = {}): WorkspaceSettingsBody => ({
  webResearchEnabled: false,
  canEdit: true,
  reportingCurrency: "CHF",
  reportingCurrencySet: false,
  fiscalYearStartMonth: null,
  budgetRates: { EUR: 0.95 },
  marketRates: { EUR: 0.9232, USD: 0.8331, GBP: 1.1044 },
  marketRatesDate: "2026-11-12",
  marketRatesSource: "ECB",
  supportedCurrencies: ["AED", "CHF", "EUR", "GBP", "USD"],
  ...over,
});
const ok = (settings: WorkspaceSettingsBody): GetWorkspaceSettingsResult => ({ ok: true, statusCode: 200, settings, error: null });

function renderSection(api: Partial<ApiClient>) {
  render(
    <MemoryRouter initialEntries={["/settings"]}>
      <I18nProvider>
        <ReportingSettings apiClient={api as ApiClient} tenantId={TENANT} />
      </I18nProvider>
    </MemoryRouter>,
  );
}

const missing: string[] = [];
beforeEach(() => {
  window.localStorage.setItem("raffa4.lang", "it");
  missing.length = 0;
  trackMissingItalian((id) => missing.push(id));
});
afterEach(() => {
  window.localStorage.clear();
  trackMissingItalian(null);
});

describe("Impostazioni — Spesa e risparmi", () => {
  it("lets an Admin set the fiscal year start, sending only what changed", async () => {
    const updateWorkspaceSettings = vi.fn().mockResolvedValue(ok(body({ fiscalYearStartMonth: 4 })));
    renderSection({ getWorkspaceSettings: vi.fn().mockResolvedValue(ok(body())), updateWorkspaceSettings });

    const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText("L’anno fiscale inizia a"), "4");
    await user.click(screen.getByRole("button", { name: "Salva le impostazioni" }));

    expect(updateWorkspaceSettings).toHaveBeenCalledWith(TENANT, { fiscalYearStartMonth: 4 });
    expect(await screen.findByText("Salvato. Spesa e risparmi usa queste impostazioni da ora.")).toBeTruthy();
    expect(missing).toEqual([]);
  });

  it("sends only the new reporting currency and crosses the reference rates into it", async () => {
    const updateWorkspaceSettings = vi.fn().mockResolvedValue(ok(body({ reportingCurrency: "EUR", reportingCurrencySet: true })));
    renderSection({ getWorkspaceSettings: vi.fn().mockResolvedValue(ok(body())), updateWorkspaceSettings });

    const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText("Valuta di riferimento"), "EUR");

    const list = screen.getByRole("list", { name: "Tassi di riferimento in EUR" });
    expect(within(list).getAllByRole("listitem")[0]!.textContent).toBe("1 CHF=1.08319 EUR");
    await user.click(screen.getByRole("button", { name: "Salva le impostazioni" }));
    expect(updateWorkspaceSettings).toHaveBeenCalledWith(TENANT, { reportingCurrency: "EUR" });
    expect(missing).toEqual([]);
  });

  it("shows the ECB reference rates read-only and lists the currencies without one", async () => {
    renderSection({ getWorkspaceSettings: vi.fn().mockResolvedValue(ok(body())), updateWorkspaceSettings: vi.fn() });

    const list = await screen.findByRole("list", { name: "Tassi di riferimento in CHF" });
    const items = within(list).getAllByRole("listitem").map((li) => li.textContent);
    expect(items).toEqual(["1 EUR=0.9232 CHF", "1 GBP=1.1044 CHF", "1 USD=0.8331 CHF"]);
    expect(screen.getByText(/tassi del 12 novembre 2026/)).toBeTruthy();
    expect(screen.getByText("Nessun tasso di riferimento per AED: i contratti in queste valute restano fuori dai totali, elencati a parte.")).toBeTruthy();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(missing).toEqual([]);
  });

  it("shows the settings read-only to a member who is not Admin", async () => {
    renderSection({ getWorkspaceSettings: vi.fn().mockResolvedValue(ok(body({ canEdit: false }))), updateWorkspaceSettings: vi.fn() });

    expect(await screen.findByText("Solo un Admin del workspace può modificare queste impostazioni.")).toBeTruthy();
    expect((screen.getByLabelText("Valuta di riferimento") as HTMLSelectElement).disabled).toBe(true);
    expect(screen.queryByRole("button", { name: "Salva le impostazioni" })).toBeNull();
    expect(screen.getByText("Non impostato (anno solare)")).toBeTruthy();
    expect(missing).toEqual([]);
  });
});
