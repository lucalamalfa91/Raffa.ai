import { beforeEach, describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import RenewalsRoute from "../../../src/routes/renewals";
import { forgetDemoViewer } from "../../../src/routes/renewals/case/demoSession";
import { getCaseStore } from "../../../src/routes/renewals/case/useCaseStore";

function Where() {
  const l = useLocation();
  return <p>at {l.pathname}</p>;
}

function renderAt(path = "/renewals", userLabel = "someone@example.test") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <I18nProvider>
        <Routes>
          <Route path="/renewals" element={<RenewalsRoute userLabel={userLabel} />} />
          <Route path="*" element={<Where />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

const cards = () => within(screen.getByRole("region", { name: "Renewals · by notice date" })).getAllByRole("listitem");

describe("Renewals: compact cards", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    forgetDemoViewer();
    getCaseStore().reset();
  });

  it("shows one card per contract, nearest notice deadline first, with the days to notice", () => {
    renderAt();
    expect(screen.getByRole("heading", { level: 2, name: "Renewals" })).toBeInTheDocument();
    expect(screen.getByText("3 renewals open · 1 not started · sorted by notice deadline")).toBeInTheDocument();
    expect(cards().map((c) => c.querySelector(".rn-supplier")?.textContent)).toEqual(["DocuSign", "Salesforce", "Microsoft", "AWS"]);
    const docusign = cards()[0] as HTMLElement;
    expect(within(docusign).getByText("24")).toBeInTheDocument();
    expect(within(docusign).getByText("ends in 53 d")).toBeInTheDocument();
    expect(within(docusign).getByText("eSignature Business Pro")).toBeInTheDocument();
    expect(within(docusign).getByText("Email to approve")).toHaveClass("tag-accent");
    expect(within(docusign).getByText("Marta Keller")).toBeInTheDocument();
  });

  it("each card draws the seven steps and says which one in words", () => {
    renderAt();
    const docusign = cards()[0] as HTMLElement;
    expect(within(docusign).getByRole("img", { name: "step 3 of 7" }).children).toHaveLength(7);
    const salesforce = cards()[1] as HTMLElement;
    expect(within(salesforce).getByRole("img", { name: "not started" })).toBeInTheDocument();
    expect(within(salesforce).getByText("Not started", { selector: ".tag" })).toHaveClass("tag-outline");
    expect(within(salesforce).getByText("No owner")).toBeInTheDocument();
  });

  it("the first card is selected and its case is shown beside the list", () => {
    renderAt();
    expect(within(cards()[0] as HTMLElement).getByRole("button")).toHaveAttribute("aria-pressed", "true");
    const panel = screen.getByRole("region", { name: "Selected renewal" });
    expect(within(panel).getByRole("heading", { level: 3, name: "DocuSign — eSignature Business Pro" })).toBeInTheDocument();
  });

  it("picking a card shows that renewal, and a contract with no case offers to start it", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(within(cards()[1] as HTMLElement).getByRole("button"));
    const panel = screen.getByRole("region", { name: "Selected renewal" });
    expect(within(panel).getByText("Not started", { selector: ".rn-case-status" })).toBeInTheDocument();
    expect(within(panel).getByText("Auto-renews for 12 months at +7% if nothing happens.")).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Start & assign to Marta" })).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "I’ll handle it" })).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Prepare non-renewal notice" })).toBeInTheDocument();
  });

  it("'Start & assign' creates the case for the buyer and the card moves into negotiation", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=1");
    await user.click(screen.getByRole("button", { name: "Start & assign to Marta" }));
    expect(getCaseStore().getState().cases["1"]).toMatchObject({ owner: "Marta Keller", approver: "Luca Lamalfa", phase: "interview" });
    const salesforce = cards().find((c) => c.textContent?.includes("Salesforce")) as HTMLElement;
    expect(within(salesforce).getByText("Interview")).toHaveClass("tag-accent");
    expect(within(salesforce).getByText("Marta Keller")).toBeInTheDocument();
  });

  it("anyone but the approver just starts the renewal, and goes to its chat", async () => {
    const user = userEvent.setup();
    renderAt("/renewals?select=1&as=Marta%20Keller");
    expect(screen.queryByRole("button", { name: /Start & assign/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Start renewal" }));
    expect(screen.getByText("at /renewals/1/chat")).toBeInTheDocument();
    expect(getCaseStore().getState().cases["1"]?.owner).toBe("Marta Keller");
  });
});

describe("Renewals: the KPI strip filters the list", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    forgetDemoViewer();
    getCaseStore().reset();
  });

  it("shows the five thresholds with their counts", () => {
    renderAt();
    const strip = screen.getByRole("group", { name: "Renewal KPIs" });
    expect(within(strip).getAllByRole("button").map((b) => b.textContent)).toEqual(["1Notice in 30 days", "2Notice in 90 days", "1Not started", "3In negotiation", "0Closed"]);
  });

  it("a cell filters the list, and pressing it again clears the filter", async () => {
    const user = userEvent.setup();
    renderAt();
    const cell = screen.getByRole("button", { name: /Notice in 90 days/ });
    await user.click(cell);
    expect(cell).toHaveAttribute("aria-pressed", "true");
    expect(cards().map((c) => c.querySelector(".rn-supplier")?.textContent)).toEqual(["DocuSign", "Salesforce"]);
    await user.click(cell);
    expect(cell).toHaveAttribute("aria-pressed", "false");
    expect(cards()).toHaveLength(4);
  });

  it("an empty filter says so in words", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: /Closed/ }));
    expect(screen.getByText("No renewal matches this filter.")).toBeInTheDocument();
  });

  it("the red number is only the urgent cell", () => {
    renderAt();
    expect(screen.getByRole("button", { name: /Notice in 30 days/ })).toHaveClass("is-urgent");
    expect(screen.getByRole("button", { name: /Notice in 90 days/ })).not.toHaveClass("is-urgent");
  });

  it("Mine / Team / Unassigned narrow the list and recount the strip", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: "Unassigned" }));
    expect(cards().map((c) => c.querySelector(".rn-supplier")?.textContent)).toEqual(["Salesforce"]);
    expect(screen.getByRole("button", { name: /Notice in 30 days/ }).textContent).toBe("0Notice in 30 days");
    await user.click(screen.getByRole("button", { name: "Mine" }));
    expect(cards().map((c) => c.querySelector(".rn-supplier")?.textContent)).toEqual(["DocuSign", "Salesforce", "AWS"]);
  });
});

describe("Renewals: views", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    forgetDemoViewer();
    getCaseStore().reset();
  });

  it("Pipeline has a column for not started and one per wizard step, with the cards in their step", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: "Pipeline" }));
    const columns = screen.getAllByRole("region").filter((r) => r.className === "rn-column");
    expect(columns.map((c) => c.getAttribute("aria-label"))).toEqual(["— · Not started", "1 · Start", "2 · Plan", "3 · Negotiation", "4 · Agreement", "5 · Draft", "6 · Signature", "7 · Acquisition"]);
    expect(within(columns[3] as HTMLElement).getByText("DocuSign")).toBeInTheDocument();
    expect(within(columns[0] as HTMLElement).getByText("Salesforce")).toBeInTheDocument();
    expect(within(columns[3] as HTMLElement).getByText("24 days")).toBeInTheDocument();
  });

  it("picking a card in the Pipeline goes back to Cases with that renewal selected", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: "Pipeline" }));
    await user.click(screen.getByRole("button", { name: /AWS/ }));
    expect(screen.getByRole("button", { name: "Cases" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("heading", { level: 3, name: "AWS — Enterprise Discount Program" })).toBeInTheDocument();
  });

  it("Action queue lists what needs doing, grouped by kind, with one primary button each", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: "Action queue" }));
    expect(screen.getAllByRole("heading", { level: 2 }).map((h) => h.textContent).filter((t) => t !== "Renewals")).toEqual(["Approvals2", "Plans & notices1", "Deadlines without owner1"]);
    const approvals = screen.getByRole("region", { name: "Approvals" });
    expect(within(approvals).getAllByRole("listitem")).toHaveLength(2);
    expect(within(approvals).getByText("Approve the counter-proposal")).toBeInTheDocument();
    expect(within(approvals).getAllByRole("button", { name: "Open at this step" })).toHaveLength(2);
  });

  it("an action can be snoozed or marked done and leaves the queue", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: "Action queue" }));
    const row = screen.getByText("Approve the counter-proposal").closest("li") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: "Snooze 3 days" }));
    expect(screen.queryByText("Approve the counter-proposal")).not.toBeInTheDocument();
    const done = screen.getByText("Notice deadline without an owner").closest("li") as HTMLElement;
    await user.click(within(done).getByRole("button", { name: "Done" }));
    expect(screen.queryByText("Notice deadline without an owner")).not.toBeInTheDocument();
    expect(getCaseStore().getState().queue.dismissed).toEqual({ "1:unassigned": true });
  });

  it("the approver can delegate an approval from the queue, and it moves on", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: "Action queue" }));
    const row = screen.getByText("Approve the counter-proposal").closest("li") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: "Delegate to Marta" }));
    expect(getCaseStore().getState().cases["4"]?.delegates).toEqual({ "Marta Keller": { close: false } });
  });

  it("opening from the queue goes to the chat at that step", async () => {
    const user = userEvent.setup();
    renderAt();
    await user.click(screen.getByRole("button", { name: "Action queue" }));
    const row = screen.getByText("Approve the counter-proposal").closest("li") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: "Open at this step" }));
    expect(screen.getByText("at /renewals/4/chat")).toBeInTheDocument();
  });
});
