import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import GlobalAskBar from "../../../src/components/ask-bar/GlobalAskBar";

function renderAtPath(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/ask" element={<div>ASK SCREEN</div>} />
        <Route path="*" element={<GlobalAskBar />} />
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

  it("submits the typed query on Enter, clears the input, and navigates to /ask", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask contigo/i });

    await userEvent.type(input, "What is overdue?{Enter}");

    expect(input).toHaveValue("");
    expect(await screen.findByText("ASK SCREEN")).toBeInTheDocument();
  });

  it("does nothing on Enter with a blank/whitespace-only query", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask contigo/i });

    await userEvent.type(input, "   {Enter}");

    expect(screen.queryByText("ASK SCREEN")).not.toBeInTheDocument();
  });

  it("asks the suggestion directly when a chip is clicked", async () => {
    renderAtPath("/");

    await userEvent.click(
      screen.getByRole("button", { name: "Which contracts renew in the next 45 days?" }),
    );

    expect(await screen.findByText("ASK SCREEN")).toBeInTheDocument();
  });

  it("focuses the input on Ctrl+K from anywhere on the screen", async () => {
    renderAtPath("/");
    const input = screen.getByRole("textbox", { name: /ask contigo/i });
    expect(input).not.toHaveFocus();

    await userEvent.keyboard("{Control>}k{/Control}");

    expect(input).toHaveFocus();
  });
});
