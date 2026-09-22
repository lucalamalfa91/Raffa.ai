import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import FeedbackCard from "../../../../src/routes/ask/reply/FeedbackCard";
import type { FeedbackOffer } from "../../../../src/routes/ask/reply/replyTypes";

const OFFER: FeedbackOffer = {
  prompt: "Vuoi segnalarlo al team Raffa.ai perché lo implementi?",
  yesLabel: "Sì",
  noLabel: "No",
  nextLabel: "Avanti",
  backLabel: "Indietro",
  submitLabel: "Invia",
  sendingLabel: "Invio in corso…",
  thanksLabel: "Grazie!",
  errorLabel: "Non sono riuscito a inviare la segnalazione. Riprova.",
  publicNotice: "Le risposte saranno pubbliche su GitHub: non inserire nomi di fornitori, importi o dati dei contratti.",
  questions: [
    { key: "what", kind: "text", label: "Cosa dovrebbe fare Raffa esattamente?", prefill: "Inviare l'email al fornitore da Raffa.ai", choices: null },
    { key: "frequency", kind: "choice", label: "Quanto spesso ti servirebbe?", prefill: null, choices: [{ key: "every-renewal", label: "ad ogni rinnovo" }, { key: "weekly", label: "ogni settimana" }] },
    { key: "importance", kind: "choice", label: "Quanto è importante per il tuo lavoro?", prefill: null, choices: [{ key: "blocking", label: "bloccante" }, { key: "nice-to-have", label: "comodo" }] },
  ],
};

// ADR-030 D5: offer → three questions one at a time → one submit; every string comes from the offer.
describe("FeedbackCard (ADR-030 D5)", () => {
  it("yes → three questions one at a time → send calls onSubmit with the three keys", async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    const user = userEvent.setup();
    render(<FeedbackCard offer={OFFER} onSubmit={onSubmit} />);

    expect(screen.getByText(OFFER.prompt)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Sì" }));

    // Q1: prefilled, editable, with the public notice.
    expect(screen.getByText(OFFER.publicNotice)).toBeInTheDocument();
    const text = screen.getByLabelText("Cosa dovrebbe fare Raffa esattamente?");
    expect(text).toHaveValue("Inviare l'email al fornitore da Raffa.ai");
    await user.clear(text);
    await user.type(text, "Inviarla dalla mia casella");
    expect(screen.queryByText("Quanto spesso ti servirebbe?")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Avanti" }));

    // Q2: chips; Next disabled until one is picked.
    expect(screen.getByText("Quanto spesso ti servirebbe?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Avanti" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "ogni settimana" }));
    await user.click(screen.getByRole("button", { name: "Avanti" }));

    // Q3: the last question submits.
    expect(screen.getByText("Quanto è importante per il tuo lavoro?")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "bloccante" }));
    await user.click(screen.getByRole("button", { name: "Invia" }));

    expect(onSubmit).toHaveBeenCalledWith({ what: "Inviarla dalla mia casella", frequency: "weekly", importance: "blocking" });
    expect(await screen.findByText("Grazie!")).toBeInTheDocument();
  });

  it("no collapses the card", async () => {
    const user = userEvent.setup();
    const { container } = render(<FeedbackCard offer={OFFER} onSubmit={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: "No" }));

    expect(container.querySelector(".reply-feedback")).toBeNull();
  });

  it("back returns to the previous question keeping its answer, and an error offers a retry", async () => {
    const onSubmit = vi.fn().mockResolvedValueOnce({ ok: false }).mockResolvedValueOnce({ ok: true });
    const user = userEvent.setup();
    render(<FeedbackCard offer={OFFER} onSubmit={onSubmit} />);

    await user.click(screen.getByRole("button", { name: "Sì" }));
    await user.click(screen.getByRole("button", { name: "Avanti" }));
    await user.click(screen.getByRole("button", { name: "ad ogni rinnovo" }));
    await user.click(screen.getByRole("button", { name: "Indietro" }));
    expect(screen.getByLabelText("Cosa dovrebbe fare Raffa esattamente?")).toHaveValue("Inviare l'email al fornitore da Raffa.ai");
    await user.click(screen.getByRole("button", { name: "Avanti" }));
    expect(screen.getByRole("button", { name: "ad ogni rinnovo" })).toHaveAttribute("aria-pressed", "true");
    await user.click(screen.getByRole("button", { name: "Avanti" }));
    await user.click(screen.getByRole("button", { name: "comodo" }));
    await user.click(screen.getByRole("button", { name: "Invia" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(OFFER.errorLabel);
    await user.click(screen.getByRole("button", { name: "Invia" }));
    expect(await screen.findByText("Grazie!")).toBeInTheDocument();
    expect(onSubmit).toHaveBeenCalledTimes(2);
  });
});
