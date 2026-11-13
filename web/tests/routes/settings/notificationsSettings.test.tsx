import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { CaseCallResult } from "../../../src/api/renewalCases";
import type { MailboxStatus, NotificationSettings } from "../../../src/api/outreach";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import NotificationsSettings from "../../../src/routes/settings/NotificationsSettings";

const ok = <T,>(body: T): CaseCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });

const settings: NotificationSettings = {
  channels: [{ id: "c1", kind: "slack", label: "Slack", enabled: true, createdAt: "2026-11-13T10:00:00Z", lastDeliveredAt: null, lastError: null }],
  available: ["email", "teams", "slack"],
  vapidPublicKey: null,
};

function renderPage(client: Partial<ApiClient>, url = "/settings/notifications?lang=en") {
  render(
    <MemoryRouter initialEntries={[url]}>
      <I18nProvider>
        <Routes>
          <Route path="/settings/notifications" element={<NotificationsSettings apiClient={client as ApiClient} />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  window.sessionStorage.setItem("raffa.signin.currentWorkspace", JSON.stringify({ id: "t1", name: "Acme" }));
});
afterEach(() => window.sessionStorage.clear());

describe("email and notifications settings", () => {
  it("offers the configured mailbox providers and adds a Teams webhook", async () => {
    const mailbox: MailboxStatus = { mailbox: null, providers: ["microsoft", "google"], available: true };
    const add = vi.fn().mockResolvedValue(ok(settings.channels[0]));
    renderPage({ getMailbox: vi.fn().mockResolvedValue(ok(mailbox)), getNotificationSettings: vi.fn().mockResolvedValue(ok(settings)), addNotificationChannel: add });

    expect(await screen.findByRole("button", { name: "Connect Microsoft 365 / Outlook" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Connect Google Workspace / Gmail" })).toBeTruthy();
    expect(screen.getByText("Slack", { selector: ".st-channel-label" })).toBeTruthy();
    await userEvent.type(screen.getByLabelText("Microsoft Teams — Workflows webhook URL"), "https://prod.logic.azure.com/wf");
    await userEvent.click(screen.getByRole("button", { name: "Add Teams" }));
    expect(add).toHaveBeenCalledWith(expect.anything(), { kind: "teams", target: "https://prod.logic.azure.com/wf", label: undefined });
  });

  it("shows the connected mailbox and the outcome of the connection", async () => {
    const mailbox: MailboxStatus = {
      mailbox: { provider: "google", address: "marta@acme.example", displayName: null, connectedAt: "2026-11-13T10:00:00Z", lastUsedAt: null, needsReconnect: false },
      providers: ["google"],
      available: true,
    };
    renderPage({ getMailbox: vi.fn().mockResolvedValue(ok(mailbox)), getNotificationSettings: vi.fn().mockResolvedValue(ok(settings)) }, "/settings/notifications?lang=en&mailbox=connected");
    expect(await screen.findByText("marta@acme.example")).toBeTruthy();
    expect(screen.getByRole("status").textContent).toContain("Mailbox connected");
    expect(screen.getByRole("button", { name: "Disconnect" })).toBeTruthy();
  });
});
