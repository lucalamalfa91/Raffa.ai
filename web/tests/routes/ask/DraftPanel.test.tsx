import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import DraftPanel, { COPIED_RESET_MS, buildMailtoHref } from "../../../src/routes/ask/DraftPanel";

const DRAFT = { subject: "Rinnovo ServiceNow – richiesta di revisione delle condizioni", body: "Gentile team ServiceNow,\n\nvi scrivo in merito al rinnovo.\n\nCordiali saluti,\n[Nome e cognome]" };

// ADR-030 D2: the email is shown verbatim in the side panel and "Copy email" copies exactly
// subject + blank line + body.
describe("DraftPanel (ADR-030 D2)", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders the subject and the body verbatim, in a labelled side panel", () => {
    const { container } = render(<DraftPanel draft={DRAFT} onClose={vi.fn()} />);

    const panel = screen.getByRole("complementary", { name: "Draft email" });
    expect(panel).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: DRAFT.subject })).toBeInTheDocument();
    const body = container.querySelector("pre.draft-document-body");
    expect(body).not.toBeNull();
    expect(body!.textContent).toBe(DRAFT.body);
    expect(screen.getByRole("button", { name: "Copy email" })).toBeInTheDocument();
  });

  it("copy writes subject and body to the clipboard, flips the label, then flips it back", async () => {
    // Same clipboard stub as MembersRoute.test.tsx's "Copy link" (fireEvent, not user-event, whose
    // setup() installs its own clipboard fake over this one).
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<DraftPanel draft={DRAFT} onClose={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Copy email" }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith(`${DRAFT.subject}\n\n${DRAFT.body}`));
    await waitFor(() => expect(screen.getByRole("button", { name: "Copied" })).toBeInTheDocument());

    act(() => {
      vi.advanceTimersByTime(COPIED_RESET_MS);
    });
    expect(screen.getByRole("button", { name: "Copy email" })).toBeInTheDocument();
  });

  it("offers the same text to the user's mail client, with no recipient", () => {
    render(<DraftPanel draft={DRAFT} onClose={vi.fn()} />);

    const link = screen.getByRole("link", { name: "Open in mail" });
    expect(link).toHaveAttribute("href", buildMailtoHref(DRAFT));
    expect(buildMailtoHref(DRAFT)).toBe(`mailto:?subject=${encodeURIComponent(DRAFT.subject)}&body=${encodeURIComponent(DRAFT.body)}`);
  });

  it("closes from the X and from Escape", () => {
    const onClose = vi.fn();
    render(<DraftPanel draft={DRAFT} onClose={onClose} />);

    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    fireEvent.keyDown(screen.getByRole("complementary", { name: "Draft email" }), { key: "Escape" });

    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("takes focus only when the user opened it", () => {
    const { unmount } = render(<DraftPanel draft={DRAFT} onClose={vi.fn()} />);
    expect(screen.getByRole("complementary", { name: "Draft email" })).not.toHaveFocus();
    unmount();

    render(<DraftPanel draft={DRAFT} focusOnOpen onClose={vi.fn()} />);
    expect(screen.getByRole("complementary", { name: "Draft email" })).toHaveFocus();
  });
});
