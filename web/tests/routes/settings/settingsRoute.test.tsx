import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { CompanyRulesBody, RuleInterviewReply } from "../../../src/api/companyRules";
import type { CaseCallResult } from "../../../src/api/renewalCases";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { trackMissingItalian } from "../../../src/i18n/translate";
import SettingsRoute from "../../../src/routes/settings";

const TENANT = "11111111-1111-1111-1111-111111111111";
const ok = <T,>(body: T): CaseCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });

const empty: CompanyRulesBody = {
  canEdit: true,
  rules: { text: "", startWindowDays: 120, sourceFile: null, updatedAt: null, updatedBy: null, isDefault: true },
};

function renderSettings(api: Partial<ApiClient>) {
  render(
    <MemoryRouter initialEntries={["/settings"]}>
      <I18nProvider>
        <Routes>
          <Route path="/settings" element={<SettingsRoute apiClient={api as ApiClient} />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

const missing: string[] = [];
beforeEach(() => {
  window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: TENANT, name: "Helvetia Foods AG" }));
  window.localStorage.setItem("raffa4.lang", "en");
  missing.length = 0;
  trackMissingItalian((id) => missing.push(id));
});
afterEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
  trackMissingItalian(null);
});

describe("Settings — company rules", () => {
  it("says the defaults apply when nothing is saved, and saves the text with its lead time", async () => {
    const saved: CompanyRulesBody = {
      canEdit: true,
      rules: { text: "No auto-renewal.", startWindowDays: 60, sourceFile: null, updatedAt: "2026-11-02T09:00:00Z", updatedBy: "Ada", isDefault: false },
    };
    const saveCompanyRules = vi.fn().mockResolvedValue(ok(saved));
    renderSettings({ getCompanyRules: vi.fn().mockResolvedValue(ok(empty)), saveCompanyRules });

    expect(await screen.findByText(/No rules saved yet\. Raffa uses its defaults.*120 days/)).toBeTruthy();
    const save = screen.getByRole("button", { name: "Save the rules" });
    expect((save as HTMLButtonElement).disabled).toBe(true);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Rules, in your own words"), "No auto-renewal.");
    const days = screen.getByLabelText(/Lead time/);
    await user.clear(days);
    await user.type(days, "60");
    await user.click(save);

    expect(saveCompanyRules).toHaveBeenCalledWith(TENANT, { text: "No auto-renewal.", startWindowDays: 60, sourceFile: null });
    expect(await screen.findByText("Saved. Raffa uses these rules from now on.")).toBeTruthy();
  });

  it("refuses a lead time out of bounds", async () => {
    renderSettings({ getCompanyRules: vi.fn().mockResolvedValue(ok(empty)), saveCompanyRules: vi.fn() });
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText("Rules, in your own words"), "x");
    const days = screen.getByLabelText(/Lead time/);
    await user.clear(days);
    await user.type(days, "5");
    expect((screen.getByRole("button", { name: "Save the rules" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("Between 14 and 365 days.")).toBeTruthy();
  });

  it("is read-only for anyone but the Head of Procurement", async () => {
    renderSettings({
      getCompanyRules: vi.fn().mockResolvedValue(ok({ ...empty, canEdit: false, rules: { ...empty.rules, text: "Net 60.", isDefault: false, updatedBy: "Ada" } })),
    });
    const area = (await screen.findByLabelText("Rules, in your own words")) as HTMLTextAreaElement;
    expect(area.disabled).toBe(true);
    expect(area.value).toBe("Net 60.");
    expect(screen.getByText("Only the Head of Procurement can change the company rules.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Start the interview" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Save the rules" })).toBeNull();
  });

  it("interviews the user and puts the proposal in the text only when they keep it", async () => {
    const replies: RuleInterviewReply[] = [
      { message: "How many days before a notice deadline?", proposal: null, startWindowDays: null, done: false },
      { message: "Check and save.", proposal: "# Company rules\n- Start 45 days before.", startWindowDays: 45, done: true },
    ];
    const interviewCompanyRules = vi.fn().mockImplementation(() => Promise.resolve(ok(replies.shift())));
    renderSettings({ getCompanyRules: vi.fn().mockResolvedValue(ok(empty)), interviewCompanyRules, saveCompanyRules: vi.fn() });

    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Start the interview" }));
    expect(await screen.findByText("How many days before a notice deadline?")).toBeTruthy();

    await user.type(screen.getByLabelText("Your answer"), "45 days");
    await user.click(screen.getByRole("button", { name: "Send" }));
    expect(interviewCompanyRules).toHaveBeenLastCalledWith(
      TENANT,
      [
        { role: "raffa", text: "How many days before a notice deadline?" },
        { role: "user", text: "45 days" },
      ],
      "",
      "en",
    );

    expect(await screen.findByText("The rules Raffa wrote")).toBeTruthy();
    expect((screen.getByLabelText("Rules, in your own words") as HTMLTextAreaElement).value).toBe("");

    await user.click(screen.getByRole("button", { name: "Use these rules" }));
    await waitFor(() => expect((screen.getByLabelText("Rules, in your own words") as HTMLTextAreaElement).value).toContain("45 days before"));
    expect((screen.getByLabelText(/Lead time/) as HTMLInputElement).value).toBe("45");
    expect((screen.getByRole("button", { name: "Save the rules" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("reads an uploaded policy into a proposal and remembers the file", async () => {
    const uploadCompanyPolicy = vi.fn().mockResolvedValue(
      ok({ fileName: "policy.pdf", text: "- Renew 90 days before.", startWindowDays: 90, summary: "I took the timing rule." }),
    );
    renderSettings({ getCompanyRules: vi.fn().mockResolvedValue(ok(empty)), uploadCompanyPolicy, saveCompanyRules: vi.fn() });

    const user = userEvent.setup();
    await screen.findByRole("button", { name: "Upload a policy" });
    const file = new File(["x"], "policy.pdf", { type: "application/pdf" });
    await user.upload(document.querySelector('input[type="file"]') as HTMLInputElement, file);

    expect(await screen.findByText("What Raffa took from policy.pdf")).toBeTruthy();
    expect(screen.getByText("I took the timing rule.")).toBeTruthy();
    expect(uploadCompanyPolicy).toHaveBeenCalledWith(TENANT, file, "en");

    await user.click(screen.getByRole("button", { name: "Use these rules" }));
    expect((screen.getByLabelText(/Lead time/) as HTMLInputElement).value).toBe("90");
  });

  it("has Italian for every message", async () => {
    window.localStorage.setItem("raffa4.lang", "it");
    renderSettings({ getCompanyRules: vi.fn().mockResolvedValue(ok(empty)), interviewCompanyRules: vi.fn() });
    expect(await screen.findByText("Regole aziendali")).toBeTruthy();
    expect(missing).toEqual([]);
  });
});
