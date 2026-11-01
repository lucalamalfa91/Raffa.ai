import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { CaseCallResult, ContractAssessment, TodayFeed, TodayItem } from "../../../src/api/renewalCases";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { trackMissingItalian } from "../../../src/i18n/translate";
import TodayRoute from "../../../src/routes/today";

const TENANT = "11111111-1111-1111-1111-111111111111";
const ok = <T,>(body: T): CaseCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });
const viewer = { userId: "u-cpo", name: "Ada Rossi", title: "Head of Procurement", isApprover: true, date: "01 Nov 2026" };

const rolled: ContractAssessment = {
  status: "rolledOver", rolledOver: true, renewedOn: "06 Aug 2026", renewedOnPassed: true, lockedOn: "07 Jul 2026",
  start: "06 Aug 2026", end: "06 Aug 2027", noticeDeadline: "07 Jul 2027", noticeDays: 248, endDays: 278,
  originalEnd: "06 Aug 2026", originalNoticeDeadline: "07 Jul 2026", renewalTermMonths: 12,
  headline: { en: "Renewed by itself on 06 Aug 2026", it: "Rinnovato da solo il 06 Ago 2026" },
  why: { en: "The notice deadline of 07 Jul 2026 has passed.", it: "La scadenza di disdetta del 07 Lug 2026 è passata." },
  recommendedAction: "prepare_next_notice", urgency: "later", source: null, origin: "ai",
};

const item = (over: Partial<TodayItem>): TodayItem => ({
  key: "c1:unassigned", kind: "unassigned", caseId: null, contractId: "c1", quoteId: null, documentId: null,
  supplier: "Splunk", contract: "Enterprise",
  title: { id: "Notice deadline without an owner" },
  why: { id: "Renews by itself for 12 months if nothing happens by {date}. Start the renewal and assign it.", vars: { date: "18 Dec 2026" } },
  due: "18 Dec 2026", dueDays: 47, dueLabel: null, stage: -1, stageLabel: { id: "Not started" }, who: "Ada Rossi", stalled: 0,
  ...over,
});

const base: TodayFeed = {
  viewer, emptyWorkspace: false, mine: [], waiting: [], snoozed: [], nextDeadline: null, assignee: null, documents: [],
};

function renderToday(feed: TodayFeed) {
  const api = { uploadSampleDocument: vi.fn(), openRenewalCase: vi.fn(), getToday: vi.fn().mockResolvedValue(ok(feed)) } as unknown as ApiClient;
  render(
    <MemoryRouter initialEntries={["/today"]}>
      <I18nProvider>
        <Routes>
          <Route path="/today" element={<TodayRoute apiClient={api} />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

async function focusCard(): Promise<HTMLElement> {
  return waitFor(() => {
    const el = document.querySelector('[data-screen-label="Focus"]');
    expect(el).not.toBeNull();
    return el as HTMLElement;
  });
}

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
});

describe("Oggi — situation once a notice deadline passed", () => {
  it("never offers a dangling 'assign to' without a real colleague", async () => {
    renderToday({ ...base, mine: [item({})] });
    const buttons = within(await focusCard()).getAllByRole("button").map((b) => b.textContent);
    expect(buttons).toEqual(["Avvia il rinnovo", "Più tardi · 3 giorni"]);
    expect(missing).toEqual([]);
  });

  it("never shows a negative day count", async () => {
    renderToday({ ...base, mine: [item({ dueDays: -117, due: "07 Jul 2026" })] });
    const focus = await focusCard();
    expect(focus.textContent).not.toContain("-117");
    expect(within(focus).getByText("07 Jul 2026")).toBeInTheDocument();
  });

  it("reads the assessment's own words and lists a renewed-by-itself contract as information", async () => {
    const info = item({
      key: "info:c2:renewed:20260806", kind: "renewedInfo", contractId: "c2", who: "",
      title: { id: "Renewed by itself on {date}", vars: { date: "06 Aug 2026" } }, assessment: rolled,
    });
    renderToday({ ...base, info: [info] });
    const box = await waitFor(() => {
      const el = document.querySelector('[data-screen-label="Per tua informazione"]');
      expect(el).not.toBeNull();
      return el as HTMLElement;
    });
    expect(within(box).getByText("Rinnovato da solo il 06 Ago 2026")).toBeInTheDocument();
    expect(within(box).getByText("La scadenza di disdetta del 07 Lug 2026 è passata.")).toBeInTheDocument();
    // Information is not "your action": no focus card, the calm state stays.
    expect(document.querySelector('[data-screen-label="Focus"]')).toBeNull();
    expect(screen.getByText("Per tua informazione")).toBeInTheDocument();
    expect(missing).toEqual([]);
  });

  it("asks to confirm the renewal term and opens the contract", async () => {
    renderToday({
      ...base,
      mine: [item({ key: "c3:confirmTerm", kind: "confirmTerm", title: { id: "Confirm the renewal term" }, dueLabel: { id: "Notice deadline passed on" }, due: "07 Jul 2026", dueDays: 0 })],
    });
    const focus = await focusCard();
    expect(within(focus).getAllByRole("button").map((b) => b.textContent)).toEqual(["Conferma la durata del rinnovo", "Più tardi · 3 giorni"]);
    expect(focus.textContent).not.toContain("0 giorni");
    expect(missing).toEqual([]);
  });
});
