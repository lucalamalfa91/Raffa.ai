import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ApiClient } from "../../../src/api/client";
import type { CaseCallResult } from "../../../src/api/renewalCases";
import type { NotificationFeed } from "../../../src/api/outreach";
import { I18nProvider } from "../../../src/i18n/I18nProvider";
import NotificationBell from "../../../src/components/shell/NotificationBell";

const ok = <T,>(body: T): CaseCallResult<T> => ({ ok: true, statusCode: 200, body, error: null });

const feed: NotificationFeed = {
  unread: 1,
  items: [
    {
      id: "n1", kind: "supplier_message", title: "New email from Splunk", body: "Splunk MSA: confirm it belongs to the negotiation to add it to the case.",
      link: "/renewals/case-1", caseId: "case-1", createdAt: "2026-11-20T09:30:00Z", read: false, vars: { supplier: "Splunk", contract: "Splunk MSA" },
    },
  ],
};

describe("the notification bell", () => {
  it("shows the unread count in Italian, opens the case and marks it read", async () => {
    window.localStorage.setItem("raffa4.lang", "it");
    const read = vi.fn().mockResolvedValue(ok(null));
    render(
      <MemoryRouter initialEntries={["/today?lang=it"]}>
        <I18nProvider>
          <Routes>
            <Route path="/today" element={<NotificationBell apiClient={{ getNotifications: vi.fn().mockResolvedValue(ok(feed)), markNotificationsRead: read } as unknown as ApiClient} tenantId="t1" />} />
            <Route path="/renewals/case-1" element={<div>case page</div>} />
          </Routes>
        </I18nProvider>
      </MemoryRouter>,
    );

    await userEvent.click(await screen.findByRole("button", { name: "Notifiche · 1 non lette" }));
    expect(screen.getByText("Nuova email da Splunk")).toBeTruthy();
    await userEvent.click(screen.getByRole("menuitem"));
    expect(read).toHaveBeenCalledWith("t1", "n1");
    expect(await screen.findByText("case page")).toBeTruthy();
    window.localStorage.clear();
  });

  it("renders nothing without the notifications API", () => {
    const { container } = render(
      <MemoryRouter>
        <I18nProvider>
          <NotificationBell apiClient={{} as ApiClient} tenantId="t1" />
        </I18nProvider>
      </MemoryRouter>,
    );
    expect(container.textContent).toBe("");
  });
});
