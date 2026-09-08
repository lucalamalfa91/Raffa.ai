import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import GlobalAskBar from "../../../src/components/ask-bar/GlobalAskBar";

/** Renders whatever router state `/ask` was reached with, so a test can assert on `newChat`/`query`
 * without `routes/ask/**` itself (this task's own "do not touch" boundary) -- a plain
 * `<div>ASK SCREEN</div>` (the pre-V2 probe) could not tell a plain navigation apart from a
 * `newChat: true` one. */
function AskProbe() {
  const location = useLocation();
  return <div>ASK SCREEN {JSON.stringify(location.state)}</div>;
}

function renderAtPath(path: string, kbReady = true) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/ask" element={<AskProbe />} />
        <Route path="*" element={<GlobalAskBar kbReady={kbReady} />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("GlobalAskBar", () => {
  it("shows the prototype's default placeholder and exactly two suggestion chips outside a known section", () => {
    renderAtPath("/");

    expect(
      screen.getByPlaceholderText("Ask Contigo — spend, renewals, clauses, liability…"),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("button")).toHaveLength(2);
  });

  it("shows contextual copy for the portfolio route", () => {
    renderAtPath("/contracts");

    expect(
      screen.getByPlaceholderText("Ask Contigo about this portfolio — clauses, dates, spend…"),
    ).toBeInTheDocument();
  });

  it("submits the typed query on Enter, clears the input, and navigates to /ask with a new-chat state (task E13/F09/US01/T01)", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask contigo/i });

    await userEvent.type(input, "What is overdue?{Enter}");

    expect(input).toHaveValue("");
    expect(await screen.findByText(/ASK SCREEN/)).toBeInTheDocument();
    expect(screen.getByText(/ASK SCREEN/)).toHaveTextContent('{"query":"What is overdue?","newChat":true}');
  });

  it("does nothing on Enter with a blank/whitespace-only query", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask contigo/i });

    await userEvent.type(input, "   {Enter}");

    expect(screen.queryByText(/ASK SCREEN/)).not.toBeInTheDocument();
  });

  it("asks the suggestion directly when a chip is clicked, also as a new chat", async () => {
    renderAtPath("/");

    await userEvent.click(
      screen.getByRole("button", { name: "Which contracts renew in the next 45 days?" }),
    );

    expect(await screen.findByText(/ASK SCREEN/)).toBeInTheDocument();
    expect(screen.getByText(/ASK SCREEN/)).toHaveTextContent('"newChat":true');
  });

  it("focuses the input on Ctrl+K from anywhere on the screen", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask contigo/i });
    expect(input).not.toHaveFocus();

    await userEvent.keyboard("{Control>}k{/Control}");

    expect(input).toHaveFocus();
  });

  describe("kbReady off (ADR-024 V2 amendment; no validated contract yet)", () => {
    it("switches the placeholder to the prototype's off-copy regardless of route", () => {
      renderAtPath("/", false);
      expect(
        screen.getByPlaceholderText("Ask Contigo switches on after your first validated contract"),
      ).toBeInTheDocument();
    });

    it("overrides even a route-contextual placeholder while off", () => {
      renderAtPath("/contracts", false);
      expect(
        screen.getByPlaceholderText("Ask Contigo switches on after your first validated contract"),
      ).toBeInTheDocument();
    });

    it("still shows the route's own suggestion chips while off (only the placeholder swaps)", () => {
      renderAtPath("/", false);
      expect(screen.getAllByRole("button")).toHaveLength(2);
    });
  });
});
