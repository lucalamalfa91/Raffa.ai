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
  available: ["email", "teams_account", "slack"],
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
  it("says connecting is optional and offers the configured mailbox providers", async () => {
    const mailbox: MailboxStatus = { mailbox: null, providers: ["microsoft"], available: true };
    renderPage({ getMailbox: vi.fn().mockResolvedValue(ok(mailbox)), getNotificationSettings: vi.fn().mockResolvedValue(ok(settings)) });

    expect(await screen.findByRole("button", { name: "Connect Microsoft 365 / Outlook" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Google/ })).toBeNull();
    expect(screen.getByText(/^Optional\./)).toBeTruthy();
    expect(screen.getByText("Connect your Microsoft account (above) to choose a Teams channel.")).toBeTruthy();
    expect(screen.getByText("Slack", { selector: ".st-channel-label" })).toBeTruthy();
  });

  it("adds a Teams channel chosen with the connected Microsoft account", async () => {
    const mailbox: MailboxStatus = {
      mailbox: { provider: "microsoft", address: "marta@acme.example", displayName: null, connectedAt: "2026-11-13T10:00:00Z", lastUsedAt: null, needsReconnect: false, canReceive: true, canPostToTeams: true, needsUpgrade: false },
      providers: ["microsoft"],
      available: true,
    };
    const add = vi.fn().mockResolvedValue(ok(settings.channels[0]));
    renderPage({
      getMailbox: vi.fn().mockResolvedValue(ok(mailbox)),
      getNotificationSettings: vi.fn().mockResolvedValue(ok({ ...settings, available: ["teams_account", "slack"] })),
      getTeamsChannels: vi.fn().mockResolvedValue(ok([{ teamId: "t1", teamName: "Procurement", channelId: "c1", channelName: "Renewals" }])),
      addNotificationChannel: add,
    });

    expect(await screen.findByText(/reads only the replies to the emails it sent for a case/)).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Choose a Teams channel" }));
    await userEvent.selectOptions(await screen.findByLabelText("Teams channel"), "t1/c1");
    await userEvent.click(screen.getByRole("button", { name: "Add Teams" }));
    expect(add).toHaveBeenCalledWith("t1", { kind: "teams_account", target: JSON.stringify({ teamId: "t1", channelId: "c1", label: "Procurement · Renewals" }), label: undefined });
  });

  it("asks to connect again when the mailbox was connected before reading replies existed", async () => {
    const mailbox: MailboxStatus = {
      mailbox: { provider: "microsoft", address: "marta@acme.example", displayName: null, connectedAt: "2026-11-13T10:00:00Z", lastUsedAt: null, needsReconnect: false, canReceive: false, canPostToTeams: false, needsUpgrade: true },
      providers: ["microsoft"],
      available: true,
    };
    renderPage({ getMailbox: vi.fn().mockResolvedValue(ok(mailbox)), getNotificationSettings: vi.fn().mockResolvedValue(ok(settings)) });
    expect(await screen.findByText("Connect again to let Raffa also read the supplier's replies and post to Teams.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Connect again" })).toBeTruthy();
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
