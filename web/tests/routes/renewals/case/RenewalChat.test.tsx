import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import RenewalChat, { type ChatTarget } from "../../../../src/routes/renewals/case/chat/RenewalChat";
import { seedDemoCases } from "../../../../src/routes/renewals/case/demoCases";
import { act, create, view } from "../../../../src/routes/renewals/case/engine";
import { DEMO_APPROVER, DEMO_OWNER, demoContext } from "../../../../src/routes/renewals/case/fixtures";
import type { Lang } from "../../../../src/i18n/translate";
import type { ActPayload, ActionType, Case } from "../../../../src/routes/renewals/case/types";

const L = DEMO_APPROVER;
const M = DEMO_OWNER;
const EVA = "Eva Roth";

/** The chat wired to the real engine, the way the route wires it to the store. */
function Wired({ initial, cid, viewer, lang = "en", onNavigate = vi.fn() }: { initial: Case; cid: string; viewer: string; lang?: Lang; onNavigate?: (t: ChatTarget) => void }) {
  const [c, setC] = useState(initial);
  const ctx = demoContext(cid, viewer, lang);
  const onAct = (type: ActionType, payload?: ActPayload) => setC((current) => act(current, type, payload, ctx));
  return <RenewalChat view={view(c, ctx)} lang={lang} onAct={onAct} onNavigate={onNavigate} />;
}

const fresh = (cid = "1", members?: string[]) => create(cid, demoContext(cid, M), null, { approver: L, owner: M, ...(members && { members }) });
const seeded = (cid: string) => {
  const c = seedDemoCases()[cid];
  if (!c) throw new Error(`no demo case ${cid}`);
  return c;
};

describe("RenewalChat: the current step only", () => {
  it("shows the case header with the seven wizard steps and the current one marked", () => {
    render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    expect(screen.getByRole("heading", { level: 2, name: "Renewal · DocuSign · eSignature Business Pro 2023–2026" })).toBeInTheDocument();
    const steps = within(screen.getByRole("list", { name: "Renewal steps" })).getAllByRole("listitem");
    expect(steps.map((s) => s.textContent)).toEqual(["1Start", "2Plan", "3Negotiation", "4Agreement", "5Draft", "6Signature", "7Acquisition"]);
    expect(steps.filter((s) => s.getAttribute("aria-current") === "step")).toHaveLength(1);
    expect(steps[2]).toHaveAttribute("aria-current", "step");
    expect(screen.getByText("02 Oct 2026 · 24 days")).toBeInTheDocument();
    expect(screen.getByText(/Luca Lamalfa · you: Approver/)).toBeInTheDocument();
  });

  it("past steps are collapsed rows above the chat; only the current step's card is open", () => {
    render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    const history = screen.getByRole("region", { name: "Steps so far" });
    const rows = within(history).getAllByRole("button");
    expect(rows.map((r) => r.textContent)).toEqual(["1Start4 items▸", "2Plan1 item▸", "3Negotiation5 items▸"]);
    rows.forEach((r) => expect(r).toHaveAttribute("aria-expanded", "false"));
    expect(screen.getByRole("heading", { level: 3, name: "Re: eSignature Business Pro — counter-proposal" })).toBeInTheDocument();
    expect(screen.queryByText("First: what do you want to do with the products and quantities?")).not.toBeInTheDocument();
  });

  it("expanding a step lists its items with date, summary and a way to open each", async () => {
    const user = userEvent.setup();
    render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    await user.click(screen.getByRole("button", { name: /Start/ }));
    const list = screen.getByRole("list", { name: "Start" });
    expect(within(list).getAllByRole("button")).toHaveLength(4);
    expect(within(list).getAllByText("View →")).toHaveLength(4);
    expect(screen.getByRole("button", { name: /Start/ })).toHaveAttribute("aria-expanded", "true");
  });

  it("answering with a chip commits it and opens the next question, without a confirm step", async () => {
    const user = userEvent.setup();
    render(<Wired initial={fresh()} cid="1" viewer={M} />);
    expect(screen.getByText("Question 1 / 4")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Keep everything as today" }));
    expect(screen.getByText("Question 2 / 4")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Keep everything as today" })).not.toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Steps so far" })).getByText("1 item")).toBeInTheDocument();
  });

  it("the clause question is multi-select and needs Continue; the plan then waits for confirmation", async () => {
    const user = userEvent.setup();
    render(<Wired initial={fresh()} cid="1" viewer={M} />);
    await user.click(screen.getByRole("button", { name: "Keep everything as today" }));
    await user.click(screen.getByRole("button", { name: "12 months" }));
    await user.click(screen.getByRole("button", { name: "No target for now" }));
    const clause = screen.getByRole("button", { name: "Limitation of liability" });
    expect(clause).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("button", { name: "Price increase" })).toHaveAttribute("aria-pressed", "true");
    await user.click(clause);
    expect(clause).toHaveAttribute("aria-pressed", "true");
    await user.click(screen.getByRole("button", { name: "Continue" }));
    expect(screen.getByRole("heading", { level: 3, name: "Renewal plan — Salesforce" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm plan" })).toBeEnabled();
  });

  it("free text answers the open question as a custom answer", async () => {
    const user = userEvent.setup();
    render(<Wired initial={fresh()} cid="1" viewer={M} />);
    await user.type(screen.getByRole("textbox", { name: "Message" }), "Keep all but cut CPQ to 40 seats{Enter}");
    expect(screen.getByText("Question 2 / 4")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Message" })).toHaveValue("");
  });

  it("ignores an empty message", async () => {
    const user = userEvent.setup();
    render(<Wired initial={fresh()} cid="1" viewer={M} />);
    await user.type(screen.getByRole("textbox", { name: "Message" }), "   {Enter}");
    expect(screen.getByText("Question 1 / 4")).toBeInTheDocument();
  });
});

describe("RenewalChat: history drawer is read only", () => {
  it("opens a past card in a dialog with no controls but Close, and Escape returns focus", async () => {
    const user = userEvent.setup();
    render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    await user.click(screen.getByRole("button", { name: /Plan/ }));
    const opener = screen.getByRole("button", { name: /Plan v1 · Renewal plan/ });
    await user.click(opener);
    const dialog = screen.getByRole("dialog", { name: "Step history" });
    expect(within(dialog).getByRole("heading", { level: 3, name: "Renewal plan — DocuSign" })).toBeInTheDocument();
    expect(within(dialog).getAllByRole("button").map((b) => b.textContent)).toEqual(["Close"]);
    expect(within(dialog).queryByRole("textbox")).not.toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Close" })).toHaveFocus();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
  });

  it("shows past answers as a record: chips cannot be pressed", async () => {
    const user = userEvent.setup();
    render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    await user.click(screen.getByRole("button", { name: /Start/ }));
    await user.click(screen.getByRole("button", { name: /Question 4 \/ 4/ }));
    const dialog = screen.getByRole("dialog");
    within(dialog)
      .queryAllByRole("button")
      .filter((b) => b.textContent !== "Close")
      .forEach((b) => expect(b).toBeDisabled());
    await user.click(within(dialog).getByRole("button", { name: "Close" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("closes from the scrim", async () => {
    const user = userEvent.setup();
    const { container } = render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    await user.click(screen.getByRole("button", { name: /Plan/ }));
    await user.click(screen.getByRole("button", { name: /Plan v1 · Renewal plan/ }));
    await user.click(container.querySelector(".rc-scrim") as HTMLElement);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

describe("RenewalChat: what each person may do", () => {
  it("a button the viewer may not press is disabled and says why, in text", () => {
    render(<Wired initial={seeded("4")} cid="4" viewer={M} />);
    const approve = screen.getByRole("button", { name: "Approve" });
    expect(approve).toBeDisabled();
    const reason = screen.getByText("Approve: Only the approver (CPO) or an explicit delegate can approve emails.");
    expect(approve).toHaveAccessibleDescription(reason.textContent ?? "");
    expect(screen.getByText(/Luca Lamalfa · you: Owner/)).toBeInTheDocument();
  });

  it("the approver sees the same button enabled, and approving moves the email on", async () => {
    const user = userEvent.setup();
    render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    await user.click(screen.getByRole("button", { name: "Approve" }));
    expect(screen.getByText("Approved — to send")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Copy email" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "I sent it" })).toBeEnabled();
  });

  it("a collaborator can read and comment but not confirm", async () => {
    const user = userEvent.setup();
    let c = fresh("1", [L, M, EVA]);
    const ctx = demoContext("1", M);
    for (const [type, p] of [["ans", { q: 0, val: "keep" }], ["ans", { q: 1, val: "12" }], ["ans", { q: 2, val: "none" }], ["q3done", {}]] as const) c = act(c, type, p, ctx);
    render(<Wired initial={c} cid="1" viewer={EVA} />);
    expect(screen.getByRole("button", { name: "Confirm plan" })).toBeDisabled();
    expect(screen.getByText("Confirm plan: Only people working on this renewal can do this.")).toBeInTheDocument();
    await user.type(screen.getByRole("textbox", { name: "Message" }), "Looks fine to me{Enter}");
    expect(screen.getByText("Looks fine to me")).toBeInTheDocument();
    expect(screen.getByText("internal comment")).toBeInTheDocument();
  });

  it("a refused command shows the reason in the header as an alert", async () => {
    const user = userEvent.setup();
    render(<Wired initial={seeded("4")} cid="4" viewer={M} />);
    await user.type(screen.getByRole("textbox", { name: "Message" }), "approve{Enter}");
    expect(screen.getByRole("alert")).toHaveTextContent("Only the approver (CPO) or an explicit delegate can approve emails.");
  });

  it("only the approver gets the team controls", async () => {
    const user = userEvent.setup();
    const { unmount } = render(<Wired initial={fresh("1", [L, M, EVA])} cid="1" viewer={L} />);
    await user.click(screen.getByRole("button", { name: /Team & delegations/ }));
    expect(screen.getByText("Eva Roth")).toBeInTheDocument();
    // Everyone but the approver can be made a delegate; only non-owners can be made the owner.
    expect(screen.getAllByRole("button", { name: "Make delegate" })).toHaveLength(2);
    expect(screen.getAllByRole("button", { name: "Reassign ownership" })).toHaveLength(1);
    await user.click(screen.getAllByRole("button", { name: "Make delegate" })[1] as HTMLElement);
    expect(screen.getByRole("button", { name: "Remove delegation" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Allow closing" })).toBeInTheDocument();
    unmount();

    render(<Wired initial={fresh("1", [L, M, EVA])} cid="1" viewer={M} />);
    await user.click(screen.getByRole("button", { name: /Team & delegations/ }));
    expect(screen.getByText("Eva Roth")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Make delegate" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reassign ownership" })).not.toBeInTheDocument();
  });
});

describe("RenewalChat: email, reply and navigation", () => {
  it("an email without a recipient asks for one and Approve stays off until it has one", async () => {
    const user = userEvent.setup();
    let c = fresh();
    const ctx = demoContext("1", M);
    for (const [type, p] of [["ans", { q: 0, val: "keep" }], ["ans", { q: 1, val: "12" }], ["ans", { q: 2, val: "none" }], ["q3done", {}], ["confirmPlan", {}]] as const) c = act(c, type, p, ctx);
    render(<Wired initial={c} cid="1" viewer={L} />);
    const approve = screen.getByRole("button", { name: "Approve" });
    expect(approve).toBeDisabled();
    expect(screen.getByText("Approve: Add the recipient first — I never invent addresses.")).toBeInTheDocument();
    await user.type(screen.getByRole("textbox", { name: "Recipient — not in the contract" }), "anna@x.test");
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
  });

  it("'Copy' does not mean sent: the email only becomes sent after the user declares it", async () => {
    const user = userEvent.setup();
    render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    await user.click(screen.getByRole("button", { name: "Approve" }));
    await user.click(screen.getByRole("button", { name: "Copy email" }));
    expect(screen.getByText("Copied — send to be confirmed")).toBeInTheDocument();
    expect(screen.queryByText(/^Sent — confirmed/)).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "I sent it" }));
    await user.click(screen.getByRole("button", { name: "Confirm sent" }));
    // The declaration is recorded, the email leaves the current step, and the next one is the check date.
    expect(screen.getByRole("heading", { level: 3, name: "When should I remind you to check for a reply?" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Negotiation/ }));
    expect(screen.getAllByText(/^Sent — confirmed by Luca Lamalfa on 08 Sep 2026/).length).toBeGreaterThan(0);
  });

  it("pasting a supplier reply opens a box with a sample and a cancel", async () => {
    const user = userEvent.setup();
    render(<Wired initial={seeded("4")} cid="4" viewer={M} />);
    await user.click(screen.getByRole("button", { name: "+ Paste reply" }));
    const box = screen.getByRole("textbox", { name: "Paste the supplier reply" });
    await user.click(screen.getByRole("button", { name: "Use sample reply" }));
    expect((box as HTMLTextAreaElement).value).toContain("thank you");
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("textbox", { name: "Paste the supplier reply" })).not.toBeInTheDocument();
  });

  it("History and + New chat leave the chat for the right place", async () => {
    const user = userEvent.setup();
    const onNavigate = vi.fn();
    render(<Wired initial={seeded("4")} cid="4" viewer={L} onNavigate={onNavigate} />);
    await user.click(screen.getByRole("button", { name: "History →" }));
    await user.click(screen.getByRole("button", { name: "+ New chat" }));
    expect(onNavigate.mock.calls.map((c) => c[0])).toEqual(["history", "newChat"]);
  });

  it("the summary since the last visit can be dismissed", async () => {
    const user = userEvent.setup();
    const store = seeded("4");
    const visited = act(store, "visit", {}, demoContext("4", L));
    render(<Wired initial={visited} cid="4" viewer={L} />);
    expect(screen.getByRole("region", { name: "Since your last visit" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Got it" }));
    expect(screen.queryByRole("region", { name: "Since your last visit" })).not.toBeInTheDocument();
  });

  it("scrolls to the current card when it opens", () => {
    const scrollTops: number[] = [];
    const proto = HTMLElement.prototype;
    const original = Object.getOwnPropertyDescriptor(proto, "offsetTop");
    Object.defineProperty(proto, "offsetTop", { configurable: true, get: () => 400 });
    const setter = vi.spyOn(proto, "scrollTop", "set").mockImplementation((v: number) => void scrollTops.push(v));
    try {
      render(<Wired initial={seeded("4")} cid="4" viewer={L} />);
    } finally {
      setter.mockRestore();
      if (original) Object.defineProperty(proto, "offsetTop", original);
      else delete (proto as { offsetTop?: number }).offsetTop;
    }
    expect(scrollTops).toContain(376);
  });
});

describe("RenewalChat: Italian", () => {
  it("renders the chrome and the cards in Italian", () => {
    render(<Wired initial={seeded("3")} cid="3" viewer={L} lang="it" />);
    expect(screen.getByRole("list", { name: "Passi del rinnovo" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Step finora" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 3, name: "Segnare queste come concordate?" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Messaggio" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Invia" })).toBeInTheDocument();
    expect(screen.getByText(/Disdetta entro/)).toBeInTheDocument();
  });
});
