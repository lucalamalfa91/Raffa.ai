import { beforeEach, describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { translator } from "../../../src/i18n/translate";
import RenewalsRoute from "../../../src/routes/renewals";
import { act, create, view } from "../../../src/routes/renewals/case/engine";
import { DEMO_APPROVER, DEMO_OWNER, demoContext } from "../../../src/routes/renewals/case/fixtures";
import { forgetDemoViewer } from "../../../src/routes/renewals/case/demoSession";
import { getCaseStore } from "../../../src/routes/renewals/case/useCaseStore";
import { nextStepLine, stepLine, tabBadges, wizardRows } from "../../../src/routes/renewals/tabs/tabModel";

const L = DEMO_APPROVER;
const M = DEMO_OWNER;
const t = translator("en");

function Where() {
  const l = useLocation();
  return <p>at {l.pathname}</p>;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <I18nProvider>
        <Routes>
          <Route path="/renewals" element={<RenewalsRoute userLabel="someone@example.test" />} />
          <Route path="*" element={<Where />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

const tabs = () => within(screen.getByRole("tablist", { name: "Renewal sections" })).getAllByRole("tab");

beforeEach(() => {
  window.localStorage.clear();
  window.sessionStorage.clear();
  forgetDemoViewer();
  getCaseStore().reset();
});

describe("case tab model", () => {
  const ctx = demoContext("1", M);
  const fresh = create("1", ctx, null, { approver: L, owner: M });

  it("counts what each tab holds; the first tab shows the step", () => {
    const v = view(act(fresh, "ans", { q: 0, val: "keep" }, ctx), ctx);
    expect(tabBadges(v)).toEqual({ now: "1/7", terms: "", timeline: String(v.upcoming.length + v.timeline.length), mail: "0", clauses: "6", team: "2" });
  });

  it("shows a tick once the case is closed", () => {
    const v = view(fresh, ctx);
    expect(tabBadges({ ...v, steps7: v.steps7.map((s) => ({ ...s, now: false })) }).now).toBe("✓");
  });

  it("the step guide puts what is done first and marks the next thing to do", () => {
    const rows = wizardRows(
      [
        { done: false, label: "Plan confirmed", who: M },
        { done: true, label: "Plan drafted by Raffa", who: "" },
        { done: false, label: "Email approved", who: L },
      ],
      L,
      t,
    );
    expect(rows.map((r) => [r.mark, r.label, r.now, r.who])).toEqual([
      ["✓", "Plan drafted by Raffa", false, "Done"],
      ["2", "Plan confirmed", true, "Who: Marta"],
      ["3", "Email approved", false, "Who: you"],
    ]);
  });

  it("a step with nothing waiting has no 'now' row", () => {
    expect(wizardRows([{ done: true, label: "Done", who: "" }], L, t).some((r) => r.now)).toBe(false);
    expect(wizardRows([], L, t)).toEqual([]);
  });

  it("says where the case is and what comes after", () => {
    const v = view(fresh, ctx);
    expect(stepLine(v, t)).toBe("Step 1 / 7 · Start");
    expect(nextStepLine(v, t)).toBe("Next step: Plan");
    const last = { ...v, steps7: v.steps7.map((s) => ({ ...s, now: s.n === 7, done: s.n < 7 })) };
    expect(nextStepLine(last, t)).toBe("Last step of the renewal");
    expect(stepLine({ ...v, steps7: v.steps7.map((s) => ({ ...s, now: false })) }, t)).toBe("Closed");
  });
});

describe("case tabs", () => {
  it("are six, always in this order, each with its count", () => {
    renderAt("/renewals?select=4");
    expect(tabs().map((tab) => tab.textContent)).toEqual(["Do now3/7", "Terms & goals6", "Timeline17", "Emails & documents2", "What to expect3", "Team2"]);
  });

  it("open on 'Do now', and picking another card goes back to it", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4&tab=terms");
    expect(tabs()[1]).toHaveAttribute("aria-selected", "true");
    await user.click(within(screen.getByRole("region", { name: "Renewals · by notice date" })).getByRole("button", { name: /AWS/ }));
    expect(tabs()[0]).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("heading", { level: 3, name: "AWS — Enterprise Discount Program" })).toBeInTheDocument();
  });

  it("?tab= opens a given tab (the chat's History link), and an unknown one falls back", () => {
    const { unmount } = renderAt("/renewals?select=4&tab=timeline");
    expect(tabs()[2]).toHaveAttribute("aria-selected", "true");
    unmount();
    renderAt("/renewals?select=4&tab=nonsense");
    expect(tabs()[0]).toHaveAttribute("aria-selected", "true");
  });

  it("each tab is a labelled panel; arrow keys move between tabs", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    const panel = screen.getByRole("tabpanel");
    expect(panel).toHaveAccessibleName(/^Do now/);
    tabs()[0]?.focus();
    await user.keyboard("{ArrowRight}");
    expect(tabs()[1]).toHaveAttribute("aria-selected", "true");
    expect(tabs()[1]).toHaveFocus();
    await user.keyboard("{End}");
    expect(tabs()[5]).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{ArrowRight}");
    expect(tabs()[0]).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{ArrowLeft}");
    expect(tabs()[5]).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{Home}");
    expect(tabs()[0]).toHaveAttribute("aria-selected", "true");
  });

  it("only the selected tab is in the tab order", () => {
    renderAt("/renewals?select=4");
    expect(tabs().map((tab) => tab.tabIndex)).toEqual([0, -1, -1, -1, -1, -1]);
  });
});

describe("tab: Do now", () => {
  it("shows the step guide with one thing to do now and one primary button", () => {
    renderAt("/renewals?select=4");
    const panel = screen.getByRole("tabpanel");
    expect(within(panel).getByText("Step 3 / 7 · Negotiation")).toBeInTheDocument();
    expect(within(panel).getByRole("heading", { level: 4, name: "Negotiation" })).toBeInTheDocument();
    const rows = within(panel).getAllByRole("listitem").filter((li) => li.className.includes("ct-row"));
    expect(rows.map((r) => (r.className.includes("is-now") ? "now" : r.className.includes("is-done") ? "done" : "later"))).toEqual(["done", "done", "done", "now", "later"]);
    const now = rows.find((r) => r.className.includes("is-now")) as HTMLElement;
    expect(within(now).getByText("Email approved by Luca Lamalfa")).toBeInTheDocument();
    expect(within(now).getByText("Drafted for Marta Keller. Nothing is sent until you approve.")).toBeInTheDocument();
    expect(within(now).getAllByRole("button").map((b) => b.textContent)).toEqual(["Open at this step", "Delegate to Marta"]);
    expect(within(panel).getByText("Next step: Agreement")).toBeInTheDocument();
  });

  it("shows days to notice and who answers for the case beside it", () => {
    renderAt("/renewals?select=4");
    const key = screen.getByRole("complementary", { name: "Key dates" });
    expect(within(key).getByText("24")).toHaveClass("is-urgent");
    expect(key.textContent).toContain("02 Oct 2026");
    expect(key.textContent).toContain("31 Oct 2026");
    expect(key.textContent).toContain("Marta Keller");
    expect(key.textContent).toContain("Luca Lamalfa");
  });

  it("when nothing waits on the viewer it says so and where to look", () => {
    renderAt("/renewals?select=4&as=Marta%20Keller");
    const panel = screen.getByRole("tabpanel");
    expect(within(panel).getByText(/Open the renewal chat|Open at the current step/)).toBeInTheDocument();
    expect(within(panel).queryByRole("button", { name: /Delegate to/ })).not.toBeInTheDocument();
  });

  it("opens the chat from the guide", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(within(screen.getByRole("tabpanel")).getByRole("button", { name: "Open at this step" }));
    expect(screen.getByText("at /renewals/4/chat")).toBeInTheDocument();
  });

  it("offers notice and alternatives while neither has been started", () => {
    renderAt("/renewals?select=4");
    const panel = screen.getByRole("tabpanel");
    expect(within(panel).getByRole("button", { name: "Prepare non-renewal notice" })).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Evaluate alternatives" })).toBeInTheDocument();
  });

  it("starting the notice from here goes to the chat on the notice", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(within(screen.getByRole("tabpanel")).getByRole("button", { name: "Prepare non-renewal notice" }));
    expect(getCaseStore().getState().cases["4"]?.notice).toMatchObject({ state: "check" });
    expect(screen.getByText("at /renewals/4/chat")).toBeInTheDocument();
  });

  it("evaluating alternatives starts them and goes to the chat", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(within(screen.getByRole("tabpanel")).getByRole("button", { name: "Evaluate alternatives" }));
    expect(getCaseStore().getState().cases["4"]?.alt).toMatchObject({ state: "req" });
    expect(screen.getByText("at /renewals/4/chat")).toBeInTheDocument();
  });

  it("delegating the approval from the guide delegates it", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(within(screen.getByRole("tabpanel")).getByRole("button", { name: "Delegate to Marta" }));
    expect(getCaseStore().getState().cases["4"]?.delegates).toEqual({ "Marta Keller": { close: false } });
  });
});

describe("tab: Terms & goals", () => {
  it("lists the terms with their state and the plan's goals", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=3");
    await user.click(tabs()[1] as HTMLElement);
    const panel = screen.getByRole("tabpanel");
    const terms = within(panel).getAllByRole("listitem").map((li) => li.textContent);
    expect(terms).toEqual(expect.arrayContaining(["Unit priceCHF 1,661,400 / yrProposed", "Term24Proposed", "Liability cap18Proposed"]));
    expect(within(panel).getByText("Plan goals")).toBeInTheDocument();
    expect(within(panel).getByText("v1 · confirmed")).toBeInTheDocument();
  });

  it("an unconfirmed plan has no terms yet", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=2");
    await user.click(tabs()[1] as HTMLElement);
    expect(screen.getByText("Terms appear once the plan is confirmed.")).toBeInTheDocument();
    expect(screen.getByText("Plan").nextElementSibling).toHaveTextContent("—"); // the interview is not finished
  });
});

describe("tab: Timeline", () => {
  it("shows what is coming before what happened, and the decision log", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(tabs()[2] as HTMLElement);
    const panel = screen.getByRole("tabpanel");
    const rows = within(panel).getAllByRole("listitem").map((li) => li.textContent ?? "");
    expect(rows[0]).toContain("Notice deadline · 24 days");
    expect(rows[1]).toContain("Term ends · 53 days");
    expect(rows[2]).toContain("08 Sep · 14:36");
    expect(within(panel).getByText("Decision log")).toBeInTheDocument();
    expect(within(panel).getByText(/Limit recorded: Stay at 12 months/)).toBeInTheDocument();
  });

  it("shows at most the latest 14 events", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=3");
    await user.click(tabs()[2] as HTMLElement);
    const past = screen.getAllByRole("listitem").filter((li) => li.className.includes("is-past"));
    expect(past).toHaveLength(14);
  });

  it("with no decisions says so", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=2");
    await user.click(tabs()[2] as HTMLElement);
    expect(screen.getByText("No decisions recorded yet.")).toBeInTheDocument();
  });
});

describe("tab: Emails & documents", () => {
  it("lists emails with their state; one waiting for a person is flagged", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(tabs()[3] as HTMLElement);
    const panel = screen.getByRole("tabpanel");
    expect(within(panel).getByText("first email · v1").nextElementSibling).toHaveTextContent("Sent");
    expect(within(panel).getByText("first email · v1").nextElementSibling).not.toHaveClass("is-alert");
    expect(within(panel).getByText("counter-proposal · v1").nextElementSibling).toHaveClass("is-alert");
  });

  it("says the contract is the only document until drafts arrive", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(tabs()[3] as HTMLElement);
    expect(screen.getByText("The current contract is the only document. Drafts appear here when they arrive.")).toBeInTheDocument();
  });

  it("a case with no email yet says so", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=2");
    await user.click(tabs()[3] as HTMLElement);
    expect(screen.getByText("No email yet.")).toBeInTheDocument();
  });
});

describe("tab: What to expect", () => {
  it("reads the contract clause by clause against general practice, never a market benchmark", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=2");
    await user.click(tabs()[4] as HTMLElement);
    const panel = screen.getByRole("tabpanel");
    expect(within(panel).getByText(/General practice, not a market benchmark/)).toBeInTheDocument();
    const table = within(panel).getByRole("table");
    expect(within(table).getAllByRole("columnheader").map((h) => h.textContent)).toEqual(["Clause", "In your contract", "Typical practice", "What to expect"]);
    expect(within(table).getAllByRole("row")).toHaveLength(5);
    expect(within(table).getByText("Price protection")).toBeInTheDocument();
  });
});

describe("tab: Team", () => {
  it("the approver can make someone the owner or delegate approvals", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4");
    await user.click(tabs()[5] as HTMLElement);
    const panel = screen.getByRole("tabpanel");
    const [luca, marta] = within(panel).getAllByRole("listitem") as [HTMLElement, HTMLElement];
    expect(luca).toHaveTextContent("Luca Lamalfa");
    expect(within(marta).getByText("Owner")).toBeInTheDocument();
    await user.click(within(marta).getByRole("button", { name: "Delegate approvals" }));
    expect(getCaseStore().getState().cases["4"]?.delegates).toEqual({ "Marta Keller": { close: false } });
    expect(within(marta).getByRole("button", { name: "Remove approval delegation" })).toBeInTheDocument();
    expect(marta).toHaveTextContent("Owner · works the case · delegate");
    expect(within(panel).getAllByRole("listitem")).toHaveLength(2);
    expect(within(luca).queryByRole("button")).not.toBeInTheDocument();
  });

  it("anyone else sees the team without the controls", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=4&as=Marta%20Keller");
    await user.click(tabs()[5] as HTMLElement);
    const panel = screen.getByRole("tabpanel");
    expect(within(panel).getByText("Luca Lamalfa")).toBeInTheDocument();
    expect(within(panel).queryByRole("button")).not.toBeInTheDocument();
  });

  it("reassigning the owner changes who owns the case", async () => {
    const user = userEvent.setup();
    getCaseStore().act("4", "delegate", { who: M, on: false }, demoContext("4", L));
    renderAt("/renewals?select=4");
    await user.click(tabs()[5] as HTMLElement);
    // Luca is the approver, so he cannot be made owner from here, but Marta can be re-confirmed.
    expect(screen.queryByRole("button", { name: "Make owner" })).not.toBeInTheDocument();
  });
});
