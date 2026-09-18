import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ApiClient, RenewalNegotiationTodoRow } from "../../api/client";
import NegotiationTodoList from "./NegotiationTodoList";

/**
 * Task E29/F04/US01/T01 (todo-web; parent story us-01-todo-web AC-1/AC-2/AC-3; closes NW-85). Proves
 * three things a pure-function test cannot: that the read-back list actually renders (topic /
 * current -> target / why / Open-Done tag), that a "Mark done" click PUTs the tick with the row's own
 * `pointKey`, and that what ends up on screen after a tick is exactly what the server returned --
 * never an optimistic local flip, never an invented point.
 */

const CONTRACT_ID = "c-1";
const TENANT_ID = "w-1";

function todoRow(overrides: Partial<RenewalNegotiationTodoRow> = {}): RenewalNegotiationTodoRow {
  return {
    contractId: CONTRACT_ID,
    pointKey: "above_band_price",
    topic: "Above-band price",
    rank: 1,
    current: "CHF 120/unit, no cap",
    target: "CHF 100/unit, capped at 5%",
    rationale: "Priced 20% above the market band for this category.",
    citationKeys: ["market:1"],
    source: "ask",
    status: "Open",
    createdAt: "2026-09-01T00:00:00Z",
    updatedAt: "2026-09-01T00:00:00Z",
    ...overrides,
  };
}

function apiClientWith(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getRenewalNegotiationTodos: vi.fn().mockResolvedValue({
      ok: true,
      statusCode: 200,
      todos: [todoRow()],
      error: null,
    }),
    tickRenewalNegotiationTodo: vi.fn(),
    ...overrides,
  } as unknown as ApiClient;
}

describe("NegotiationTodoList (NW-85)", () => {
  it("AC-1: renders the read-back TODO list -- topic, current -> target, why, and an Open tag", async () => {
    const apiClient = apiClientWith();

    render(<NegotiationTodoList apiClient={apiClient} tenantId={TENANT_ID} contractId={CONTRACT_ID} />);

    expect(await screen.findByText("Above-band price")).toBeInTheDocument();
    expect(screen.getByText(/CHF 120\/unit, no cap → CHF 100\/unit, capped at 5%/)).toBeInTheDocument();
    expect(screen.getByText(/Priced 20% above the market band/)).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(apiClient.getRenewalNegotiationTodos).toHaveBeenCalledWith(TENANT_ID, CONTRACT_ID);
  });

  it("never invents a point: a Superseded row never renders as a TODO", async () => {
    const apiClient = apiClientWith({
      getRenewalNegotiationTodos: vi.fn().mockResolvedValue({
        ok: true,
        statusCode: 200,
        todos: [todoRow({ pointKey: "payment_terms", topic: "Payment terms", status: "Superseded" })],
        error: null,
      }),
    });

    render(<NegotiationTodoList apiClient={apiClient} tenantId={TENANT_ID} contractId={CONTRACT_ID} />);

    expect(await screen.findByText("No negotiation points yet.")).toBeInTheDocument();
    expect(screen.queryByText("Payment terms")).not.toBeInTheDocument();
  });

  it("AC-2/AC-3: Mark done PUTs the tick with this row's pointKey and renders exactly the server's returned Done state", async () => {
    const user = userEvent.setup();
    const ticked = todoRow({ status: "Done" });
    const apiClient = apiClientWith({
      tickRenewalNegotiationTodo: vi.fn().mockResolvedValue({ ok: true, statusCode: 200, todo: ticked, error: null }),
    });

    render(<NegotiationTodoList apiClient={apiClient} tenantId={TENANT_ID} contractId={CONTRACT_ID} />);

    const markDone = await screen.findByRole("button", { name: "Mark Above-band price done" });
    await user.click(markDone);

    expect(apiClient.tickRenewalNegotiationTodo).toHaveBeenCalledWith(TENANT_ID, CONTRACT_ID, {
      pointKey: "above_band_price",
    });

    // The row now reads Done -- and, since the row was Done, the Mark-done button is gone (nothing
    // left to do), not merely relabelled.
    expect(await screen.findByText("Done")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mark Above-band price done" })).not.toBeInTheDocument();
  });

  it("a failed tick surfaces the server's own error and leaves the row Open", async () => {
    const user = userEvent.setup();
    const apiClient = apiClientWith({
      tickRenewalNegotiationTodo: vi.fn().mockResolvedValue({
        ok: false,
        statusCode: 404,
        todo: null,
        error: "No negotiation TODO found for point 'above_band_price' on contract c-1.",
      }),
    });

    render(<NegotiationTodoList apiClient={apiClient} tenantId={TENANT_ID} contractId={CONTRACT_ID} />);

    await user.click(await screen.findByRole("button", { name: "Mark Above-band price done" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/No negotiation TODO found/);
    // Never invents a Done the server did not confirm.
    expect(screen.getByText("Open")).toBeInTheDocument();
  });
});
