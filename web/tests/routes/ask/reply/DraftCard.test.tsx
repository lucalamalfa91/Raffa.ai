import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import DraftCard from "../../../../src/routes/ask/reply/DraftCard";

const DRAFT = { subject: "Rinnovo ServiceNow – richiesta di revisione delle condizioni", body: "Gentile team ServiceNow,\n\nvi scrivo in merito al rinnovo.\n\nCordiali saluti,\n[Nome e cognome]" };

// ADR-030 D2: the email is shown verbatim and "Copy email" copies exactly subject + blank line + body.
describe("DraftCard (ADR-030 D2)", () => {
  it("renders the subject and the body verbatim in a pre-wrap block", () => {
    const { container } = render(<DraftCard draft={DRAFT} />);

    expect(screen.getByText(/Rinnovo ServiceNow – richiesta di revisione delle condizioni/)).toBeInTheDocument();
    const body = container.querySelector("pre.reply-draft-body");
    expect(body).not.toBeNull();
    expect(body!.textContent).toBe(DRAFT.body);
    expect(screen.getByRole("button", { name: "Copy email" })).toBeInTheDocument();
  });

  it("copy writes subject and body to the clipboard and flips the label", async () => {
    // Same clipboard stub as MembersRoute.test.tsx's "Copy link" (fireEvent, not user-event, whose
    // setup() installs its own clipboard fake over this one).
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    render(<DraftCard draft={DRAFT} />);

    fireEvent.click(screen.getByRole("button", { name: "Copy email" }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith(`${DRAFT.subject}\n\n${DRAFT.body}`));
    await waitFor(() => expect(screen.getByRole("button", { name: "Copied" })).toBeInTheDocument());
  });
});
