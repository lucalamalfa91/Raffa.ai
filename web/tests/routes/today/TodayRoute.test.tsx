import { beforeEach, describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { demoContext } from "../../../src/routes/renewals/case/fixtures";
import { forgetDemoViewer } from "../../../src/routes/renewals/case/demoSession";
import { getCaseStore } from "../../../src/routes/renewals/case/useCaseStore";
import TodayRoute from "../../../src/routes/today";

function Where() {
  const l = useLocation();
  return (
    <p>
      at {l.pathname}
      {l.search}
    </p>
  );
}

function renderAt(path = "/today", userLabel = "someone@example.test") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <I18nProvider>
        <Routes>
          <Route path="/today" element={<TodayRoute userLabel={userLabel} />} />
          <Route path="*" element={<Where />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

const rows = () => screen.getAllByRole("listitem").filter((li) => li.className === "rn-qrow");
const titles = () => rows().map((r) => r.querySelector(".rn-qtitle")?.textContent);

beforeEach(() => {
  window.localStorage.clear();
  window.sessionStorage.clear();
  forgetDemoViewer();
  getCaseStore().reset();
});

describe("Today", () => {
  it("opens with who is looking, today's date and how much waits on them", () => {
    renderAt();
    expect(screen.getByText("Luca Lamalfa · Head of Procurement")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Today · 08 Sep 2026" })).toBeInTheDocument();
    expect(screen.getByText("3 actions waiting for you · ordered by notice deadline")).toBeInTheDocument();
  });

  it("is a queue ordered by urgency, in three bands by days to the notice deadline", () => {
    renderAt();
    const bands = screen.getAllByRole("region").map((r) => r.getAttribute("aria-label"));
    expect(bands).toEqual(["Within 30 days", "31–120 days", "Later"]);
    expect(titles()).toEqual(["Approve the counter-proposal", "Notice deadline without an owner", "Policy exception: liability 18 vs 24 months"]);
    const first = rows()[0] as HTMLElement;
    expect(within(first).getByText("24")).toHaveClass("is-urgent");
    expect(within(first).getByText("days to notice · 02 Oct 2026")).toBeInTheDocument();
    expect(first.textContent).toContain("DocuSign · eSignature Business Pro · Step 3 / 7 · Negotiation");
    expect(within(rows()[2] as HTMLElement).getByText("266")).not.toHaveClass("is-urgent");
  });

  it("each row says who it waits on, and has one primary button", () => {
    renderAt();
    const first = rows()[0] as HTMLElement;
    expect(within(first).getByText("Waiting for you")).toHaveClass("tag-accent");
    expect(within(first).getByRole("button", { name: "Open at this step" })).toHaveClass("btn-primary");
  });

  it("Mine / Team / Unassigned each carry their count", () => {
    renderAt();
    const tabs = screen.getByRole("group", { name: "Show" });
    expect(within(tabs).getAllByRole("button").map((b) => b.textContent)).toEqual(["Mine · 3", "Team · 1", "Unassigned · 1"]);
  });

  it("Team lists what waits on others, with how long a case has stood still", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: /Team/ }));
    expect(titles()).toEqual(["Complete the renewal plan"]);
    const row = rows()[0] as HTMLElement;
    expect(within(row).getByText("Waiting for Marta Keller")).toHaveClass("tag-outline");
    expect(within(row).getByText("No activity for 9 days")).toBeInTheDocument();
    expect(within(row).getByRole("button", { name: "Open the case" })).toBeInTheDocument();
  });

  it("Unassigned lists the deadlines nobody owns", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: /Unassigned/ }));
    expect(titles()).toEqual(["Notice deadline without an owner"]);
  });

  it("opening a row goes to the chat at that step", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(within(rows()[0] as HTMLElement).getByRole("button", { name: "Open at this step" }));
    expect(screen.getByText("at /renewals/4/chat")).toBeInTheDocument();
  });

  it("'Open in Renewals' lands on that case's first tab", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(within(rows()[0] as HTMLElement).getByRole("button", { name: "Open in Renewals" }));
    expect(screen.getByText("at /renewals?select=4&tab=now")).toBeInTheDocument();
  });

  it("a deadline with no case has no 'Open in Renewals' (there is nothing to open yet)", () => {
    renderAt();
    expect(within(rows()[1] as HTMLElement).queryByRole("button", { name: "Open in Renewals" })).not.toBeInTheDocument();
  });

  it("the approver starts and assigns an unowned renewal; it leaves Unassigned and joins the team", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(within(rows()[1] as HTMLElement).getByRole("button", { name: "Start & assign to Marta" }));
    expect(getCaseStore().getState().cases["1"]).toMatchObject({ owner: "Marta Keller", phase: "interview" });
    expect(within(screen.getByRole("group", { name: "Show" })).getAllByRole("button").map((b) => b.textContent)).toEqual(["Mine · 2", "Team · 2", "Unassigned · 0"]);
  });

  it("delegating an approval hands it to the other person", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(within(rows()[0] as HTMLElement).getByRole("button", { name: "Delegate to Marta" }));
    expect(getCaseStore().getState().cases["4"]?.delegates).toEqual({ "Marta Keller": { close: false } });
    // The approval now waits on the delegate, so it is no longer on the approver's own list.
    expect(within(screen.getByRole("group", { name: "Show" })).getAllByRole("button")[0]).toHaveTextContent("Mine · 2");
  });
});

describe("Today: snooze and done", () => {
  it("snoozing hides a row for three days and keeps it in a list to bring back", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(within(rows()[0] as HTMLElement).getByRole("button", { name: "Snooze 3 days" }));
    expect(titles()).not.toContain("Approve the counter-proposal");
    const snoozed = screen.getByRole("region", { name: "Snoozed" });
    expect(within(snoozed).getByText("Approve the counter-proposal")).toBeInTheDocument();
    expect(within(snoozed).getByText("DocuSign · eSignature Business Pro · until 11 Sep 2026")).toBeInTheDocument();
    await user.click(within(snoozed).getByRole("button", { name: "Bring back" }));
    expect(titles()[0]).toBe("Approve the counter-proposal");
    expect(screen.queryByRole("region", { name: "Snoozed" })).not.toBeInTheDocument();
  });

  it("marking done removes a row for good and remembers it", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(within(rows()[0] as HTMLElement).getByRole("button", { name: "Done" }));
    expect(titles()).not.toContain("Approve the counter-proposal");
    expect(screen.queryByRole("region", { name: "Snoozed" })).not.toBeInTheDocument();
    expect(JSON.parse(window.localStorage.getItem("raffa.renewalCases.v3") ?? "{}").queue.dismissed).toEqual({ "4:approve:Approve the counter-proposal": true });
  });

  it("an empty tab says why in words", async () => {
    const user = userEvent.setup();
    renderAt("/today?as=Marta%20Keller");
    expect(screen.getByText("Marta Keller · Buyer")).toBeInTheDocument();
    await user.click(within(rows()[0] as HTMLElement).getByRole("button", { name: "Done" }));
    expect(screen.getByText("Nothing needs you right now. When a colleague finishes a step that needs your approval, it appears here.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Unassigned/ }));
    expect(titles()).toEqual(["Notice deadline without an owner"]); // Salesforce still has no case
    getCaseStore().open("1", demoContext("1", "Marta Keller"), { approver: "Luca Lamalfa", owner: "Marta Keller" });
    expect(await screen.findByText("Every notice deadline has an owner.")).toBeInTheDocument();
  });

  it("a person who is not the approver cannot delegate, and starts a renewal for themselves", async () => {
    const user = userEvent.setup();
    renderAt("/today?as=Marta%20Keller");
    expect(screen.queryByRole("button", { name: /Delegate to/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Team/ }));
    expect(titles()).toEqual(["Approve the counter-proposal", "Policy exception: liability 18 vs 24 months"]);
  });

  it("ends with the next deadline and what the team has in progress", () => {
    renderAt();
    expect(screen.getByText("Next notice deadline: DocuSign in 24 days (02 Oct 2026) · 1 in progress with the team")).toBeInTheDocument();
  });
});
