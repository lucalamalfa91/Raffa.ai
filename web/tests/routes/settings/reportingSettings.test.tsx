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
  supportedCurrencies: ["CHF", "EUR", "GBP", "USD"],
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
  it("lets an Admin set the fiscal year start and add a budget rate, sending only what changed", async () => {
    const updateWorkspaceSettings = vi.fn().mockResolvedValue(ok(body({ fiscalYearStartMonth: 4, budgetRates: { EUR: 0.95, GBP: 1.12 } })));
    renderSection({ getWorkspaceSettings: vi.fn().mockResolvedValue(ok(body())), updateWorkspaceSettings });

    const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText("L’anno fiscale inizia a"), "4");
    await user.click(screen.getByRole("button", { name: "Aggiungi un cambio di budget" }));
    const rows = screen.getByRole("group", { name: "Cambi di budget" });
    const codes = within(rows).getAllByLabelText("Valuta");
    await user.selectOptions(codes[1]!, "GBP");
    await user.type(within(rows).getAllByLabelText("Cambio in CHF")[1]!, "1,12");
    await user.click(screen.getByRole("button", { name: "Salva le impostazioni" }));

    expect(updateWorkspaceSettings).toHaveBeenCalledWith(TENANT, { fiscalYearStartMonth: 4, budgetRates: { EUR: 0.95, GBP: 1.12 } });
    expect(await screen.findByText("Salvato. Spesa e risparmi usa queste impostazioni da ora.")).toBeTruthy();
    expect(missing).toEqual([]);
  });

  it("clears the rates when the reporting currency changes, and says why", async () => {
    const updateWorkspaceSettings = vi.fn().mockResolvedValue(ok(body({ reportingCurrency: "EUR", reportingCurrencySet: true, budgetRates: {} })));
    renderSection({ getWorkspaceSettings: vi.fn().mockResolvedValue(ok(body())), updateWorkspaceSettings });

    const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText("Valuta di riferimento"), "EUR");

    expect(screen.getByText("I cambi di budget erano rispetto a CHF: inseriscili di nuovo rispetto a EUR.")).toBeTruthy();
    expect(screen.queryByLabelText("Cambio in EUR")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Salva le impostazioni" }));
    expect(updateWorkspaceSettings).toHaveBeenCalledWith(TENANT, { reportingCurrency: "EUR", budgetRates: {} });
    expect(missing).toEqual([]);
  });

  it("does not save a rate that is not a positive number", async () => {
    const updateWorkspaceSettings = vi.fn();
    renderSection({ getWorkspaceSettings: vi.fn().mockResolvedValue(ok(body())), updateWorkspaceSettings });

    const user = userEvent.setup();
    const rate = await screen.findByLabelText("Cambio in CHF");
    await user.clear(rate);
    await user.type(rate, "0");

    expect((screen.getByRole("button", { name: "Salva le impostazioni" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("Ogni cambio richiede una valuta e un numero maggiore di 0.")).toBeTruthy();
    expect(updateWorkspaceSettings).not.toHaveBeenCalled();
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
