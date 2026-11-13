import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { CaseCallResult, CaseCardEmail, CaseEnvelope } from "../../../src/api/renewalCases";
import type { CaseMailStatus } from "../../../src/api/outreach";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import { CaseMail } from "../../../src/routes/renewals/case/CaseMail";

const ok = <T,>(body: T): CaseCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });

const email = (stateKey: string): CaseCardEmail => ({
  eid: 0, to: "anna@splunk.example", toMissing: false, toKey: "to:0", stateKey, state: "Approved", subject: "Splunk MSA",
  body: "Dear Splunk team", attach: ["contract.pdf"], editing: false, editBody: "", showBody: true, sentNote: "", attachNote: "",
});

const status = (over: Partial<CaseMailStatus> = {}): CaseMailStatus => ({
  caseAddress: "r-abc123def456@in.raffa.test",
  mailbox: { provider: "microsoft", address: "marta@acme.example", displayName: "Marta", connectedAt: "2026-11-13T10:00:00Z", lastUsedAt: null, needsReconnect: false },
  mailboxAvailable: true,
  inbound: [],
  ...over,
});

function renderMail(client: Partial<ApiClient>, mail: CaseCardEmail | null, onCase = vi.fn()) {
  render(
    <MemoryRouter initialEntries={["/renewals/case-1?lang=en"]}>
      <I18nProvider>
        <CaseMail apiClient={client as ApiClient} tenantId="t1" caseId="case-1" version={7} canWork email={mail} onCase={onCase} />
      </I18nProvider>
    </MemoryRouter>,
  );
  return onCase;
}

describe("the case mail", () => {
  it("sends the approved email from the connected mailbox and says the case address is in copy", async () => {
    const next = { caseId: "case-1", version: 8 } as CaseEnvelope;
    const send = vi.fn().mockResolvedValue(ok(next));
    const onCase = renderMail({ getCaseMail: vi.fn().mockResolvedValue(ok(status())), sendCaseEmail: send }, email("approved"));

    await userEvent.click(await screen.findByRole("button", { name: "Send from marta@acme.example" }));
    expect(screen.getByText(/r-abc123def456@in\.raffa\.test in copy/)).toBeTruthy();
    expect(send).toHaveBeenCalledWith("t1", "case-1", 0, 7);
    expect(onCase).toHaveBeenCalledWith(next);
  });

  it("offers to connect a mailbox when none is connected, and nothing to send for a draft", async () => {
    renderMail({ getCaseMail: vi.fn().mockResolvedValue(ok(status({ mailbox: null }))) }, email("approved"));
    expect((await screen.findByRole("link", { name: "connect your mailbox" })).getAttribute("href")).toBe("/settings/notifications");
  });

  it("asks the owner to confirm a supplier email before it enters the case", async () => {
    const pending = { id: "m1", from: "Anna <anna@splunk.example>", subject: "Re: Splunk MSA", receivedAt: "2026-11-20T09:30:00Z", preview: "We can offer 5% off.", attachments: ["draft.pdf"], state: "pending", decidedBy: null, decidedAt: null };
    const accept = vi.fn().mockResolvedValue(ok({ caseId: "case-1", version: 8 } as CaseEnvelope));
    const dismiss = vi.fn().mockResolvedValue(ok(null));
    renderMail({ getCaseMail: vi.fn().mockResolvedValue(ok(status({ inbound: [pending] }))), acceptInboundMail: accept, dismissInboundMail: dismiss }, email("sent"));

    expect(await screen.findByText("We can offer 5% off.")).toBeTruthy();
    expect(screen.getByText("Attachments: draft.pdf")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Send from/ })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "It belongs to this negotiation — add it" }));
    expect(accept).toHaveBeenCalledWith("t1", "case-1", "m1", 7, "en");
  });

  it("explains a refused send", async () => {
    const send = vi.fn().mockResolvedValue({ ok: false, statusCode: 400, body: null, error: "x", code: "mailbox_reconnect", current: null });
    renderMail({ getCaseMail: vi.fn().mockResolvedValue(ok(status())), sendCaseEmail: send }, email("copied"));
    await userEvent.click(await screen.findByRole("button", { name: "Send from marta@acme.example" }));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Your mailbox connection has expired. Connect it again, then send.");
  });
});
