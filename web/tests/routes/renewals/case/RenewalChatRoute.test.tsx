import { beforeEach, describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { I18nProvider } from "../../../../src/i18n/I18nProvider";
import RenewalChatRoute from "../../../../src/routes/renewals/case/RenewalChatRoute";
import { getCaseStore } from "../../../../src/routes/renewals/case/useCaseStore";

function Where() {
  const location = useLocation();
  return <p>at {location.pathname}{location.search}</p>;
}

function renderAt(path: string, userLabel = "someone@example.test") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <I18nProvider>
        <Routes>
          <Route path="/renewals/:contractId/chat" element={<RenewalChatRoute userLabel={userLabel} />} />
          <Route path="*" element={<Where />} />
        </Routes>
      </I18nProvider>
    </MemoryRouter>,
  );
}

describe("RenewalChatRoute", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    getCaseStore().reset();
  });

  it("opens the wizard for a contract that has a case, as its approver by default", () => {
    renderAt("/renewals/4/chat");
    expect(screen.getByRole("heading", { level: 2, name: /Renewal · DocuSign/ })).toBeInTheDocument();
    expect(screen.getByText(/Luca Lamalfa · you: Approver/)).toBeInTheDocument();
  });

  it("starts a case for a contract that has none, owned by the person opening it", () => {
    renderAt("/renewals/1/chat?as=Marta%20Keller");
    expect(screen.getByRole("heading", { level: 2, name: /Renewal · Salesforce/ })).toBeInTheDocument();
    expect(screen.getByText("Question 1 / 4")).toBeInTheDocument();
    expect(getCaseStore().getState().cases["1"]).toMatchObject({ owner: "Marta Keller", approver: "Luca Lamalfa" });
    expect(screen.getByText(/you: Owner/)).toBeInTheDocument();
  });

  it("?as= picks a person on the case; anyone else is the approver", () => {
    const { unmount } = renderAt("/renewals/4/chat?as=Marta%20Keller");
    expect(screen.getByText(/you: Owner/)).toBeInTheDocument();
    unmount();
    renderAt("/renewals/4/chat?as=Mallory");
    expect(screen.getByText(/you: Approver/)).toBeInTheDocument();
  });

  it("a signed-in account that is on the case is itself", () => {
    renderAt("/renewals/4/chat?as=Luca%20Lamalfa", "Marta Keller");
    expect(screen.getByText(/you: Owner/)).toBeInTheDocument();
  });

  it("shows Italian with ?lang=it and English otherwise", () => {
    const { unmount } = renderAt("/renewals/4/chat?lang=it");
    expect(screen.getByRole("heading", { level: 2, name: /Rinnovo · DocuSign/ })).toBeInTheDocument();
    unmount();
    // The choice is remembered, so a plain visit stays Italian until English is asked for.
    renderAt("/renewals/4/chat");
    expect(screen.getByRole("heading", { level: 2, name: /Rinnovo · DocuSign/ })).toBeInTheDocument();
    unmount();
    window.localStorage.clear();
    renderAt("/renewals/4/chat");
    expect(screen.getByRole("heading", { level: 2, name: /Renewal · DocuSign/ })).toBeInTheDocument();
  });

  it("keeps what the user does in the store", async () => {
    const user = userEvent.setup();
    renderAt("/renewals/4/chat");
    await user.click(screen.getByRole("button", { name: "Approve" }));
    expect(getCaseStore().getState().cases["4"]?.emails[1]?.state).toBe("approved");
    expect(JSON.parse(window.localStorage.getItem("raffa.renewalCases.v2") ?? "{}").cases["4"].emails[1].state).toBe("approved");
  });

  it("an unknown contract says so and links back to Renewals", () => {
    renderAt("/renewals/not-a-contract/chat");
    expect(screen.getByRole("heading", { name: "This renewal is not available" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to Renewals" })).toHaveAttribute("href", "/renewals");
    expect(getCaseStore().getState().cases["not-a-contract"]).toBeUndefined();
  });

  it("History goes to the Renewals case on its timeline; + New chat goes to Ask", async () => {
    const user = userEvent.setup();
    const { unmount } = renderAt("/renewals/4/chat");
    await user.click(screen.getByRole("button", { name: "History →" }));
    expect(screen.getByText("at /renewals?select=4&tab=timeline")).toBeInTheDocument();
    unmount();
    renderAt("/renewals/4/chat");
    await user.click(screen.getByRole("button", { name: "+ New chat" }));
    expect(screen.getByText("at /ask")).toBeInTheDocument();
  });

  it("opening the case twice in a row records one visit", () => {
    renderAt("/renewals/4/chat");
    const first = getCaseStore().getState().cases["4"]?.ui.since;
    expect(first?.n).toBeGreaterThan(0);
    expect(screen.getByRole("region", { name: "Since your last visit" })).toBeInTheDocument();
  });
});
