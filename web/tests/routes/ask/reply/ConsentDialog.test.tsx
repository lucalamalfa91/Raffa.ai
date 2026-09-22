import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ConsentDialog from "../../../../src/routes/ask/reply/ConsentDialog";
import type { InterviewQuestion, InterviewReply } from "../../../../src/routes/ask/reply/replyTypes";

const QUESTION: InterviewQuestion = {
  key: "web-consent",
  prompt: "Raffa will search the public web for: “typical uplift caps on saas renewals”. Nothing from your contracts leaves Raffa. The results are not verified. Allow?",
  presentation: "consent",
  allowFreeText: false,
  options: [
    { key: "allow", label: "Yes, search the web", hint: "One search, for this question only." },
    { key: "decline", label: "No, stay in Raffa", hint: "I answer from your contracts only." },
  ],
};

const REPLY: InterviewReply = {
  kind: "interview",
  prompt: QUESTION.prompt,
  questions: [QUESTION],
  answered: false,
  messageId: "msg-consent",
};

function renderDialog() {
  const onDecide = vi.fn();
  render(<ConsentDialog reply={REPLY} question={QUESTION} onDecide={onDecide} />);
  return { onDecide };
}

describe("ConsentDialog (ADR-030)", () => {
  it("is a modal alert dialog that shows the exact query and starts focused on the safe choice", () => {
    renderDialog();

    const dialog = screen.getByRole("alertdialog", { name: "Search the public web?" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAccessibleDescription(/typical uplift caps on saas renewals/);
    expect(screen.getByRole("button", { name: "No, stay in Raffa" })).toHaveFocus();
    expect(screen.getByRole("button", { name: "Yes, search the web" })).toHaveClass("btn-primary");
  });

  it("reports the allow option on Yes and the decline option on No", async () => {
    const user = userEvent.setup();
    const { onDecide } = renderDialog();

    await user.click(screen.getByRole("button", { name: "Yes, search the web" }));
    expect(onDecide).toHaveBeenLastCalledWith(REPLY, QUESTION, expect.objectContaining({ key: "allow" }));

    await user.click(screen.getByRole("button", { name: "No, stay in Raffa" }));
    expect(onDecide).toHaveBeenLastCalledWith(REPLY, QUESTION, expect.objectContaining({ key: "decline" }));
  });

  it("Escape declines and Tab cycles inside the dialog", async () => {
    const user = userEvent.setup();
    const { onDecide } = renderDialog();

    await user.tab();
    expect(screen.getByRole("button", { name: "Yes, search the web" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "No, stay in Raffa" })).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(onDecide).toHaveBeenCalledTimes(1);
    expect(onDecide.mock.calls[0][2].key).toBe("decline");
  });

  it("renders nothing for a question without the two consent options", () => {
    const { container } = render(
      <ConsentDialog reply={REPLY} question={{ ...QUESTION, options: [QUESTION.options[0]] }} onDecide={vi.fn()} />,
    );

    expect(container.querySelector("[role='alertdialog']")).toBeNull();
  });
});
