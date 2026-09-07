import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import SignInScreen from "../../../src/routes/signin/SignInScreen";

describe("SignInScreen", () => {
  it("shows the idle 'Continue with Microsoft Entra ID' affordance", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    const button = screen.getByRole("button", { name: /continue with microsoft entra id/i });
    expect(button).toBeInTheDocument();
    expect(button).not.toBeDisabled();
  });

  it("calls onContinue and switches to the redirecting state when clicked", async () => {
    const onContinue = vi.fn();
    render(<SignInScreen onContinue={onContinue} interactionInFlight={false} />);

    await userEvent.click(screen.getByRole("button", { name: /continue with microsoft entra id/i }));

    expect(onContinue).toHaveBeenCalledTimes(1);
    const button = screen.getByRole("button", { name: /redirecting to microsoft entra id/i });
    expect(button).toBeDisabled();
  });

  it("renders the redirecting state up front when MSAL already has an interaction in flight", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={true} />);

    expect(screen.getByRole("button", { name: /redirecting to microsoft entra id/i })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /^continue with microsoft entra id$/i })).not.toBeInTheDocument();
  });

  it("renders the north-star statement and the four V1 job cells", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    expect(screen.getByText(/what we bought/i)).toBeInTheDocument();
    expect(screen.getByText(/where we can save money/i)).toBeInTheDocument();
    expect(screen.getByText("Contract")).toBeInTheDocument();
    expect(screen.getByText("Renewal")).toBeInTheDocument();
    expect(screen.getByText("Savings")).toBeInTheDocument();
    expect(screen.getByText("New purchase")).toBeInTheDocument();
    // Task E11/F02/US01/T01 (gap G-S1-JOBS): Contract, Renewal, and Savings
    // all pair with the *same* bold word "Intelligence" in the compiled
    // export -- three cells, not one, now render it.
    expect(screen.getAllByText("Intelligence")).toHaveLength(3);
    expect(screen.getByText("Quote Check")).toBeInTheDocument();
  });

  it("tells a not-yet-provisioned user to ask their Workspace Admin", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    expect(screen.getByText(/ask your workspace admin for an invitation/i)).toBeInTheDocument();
  });

  // Task E11/F02/US01/T01 (signin-1to1) -- gaps G-S1-LOCKUP/G-S1-RIGHT.
  it("renders the Contigo lockup (accent mark + wordmark) above the north-star", () => {
    const { container } = render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    const lockup = container.querySelector(".signin-lockup");
    expect(lockup).toBeInTheDocument();
    expect(lockup).toHaveTextContent("Contigo");
    expect(container.querySelector(".signin-lockup-mark")).toBeInTheDocument();
  });

  it("heads the right column 'Sign in' (h2), not a 'Contigo' heading", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    expect(screen.getByRole("heading", { name: "Sign in", level: 2 })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Contigo" })).not.toBeInTheDocument();
  });

  it("shows the muted Entra sign-in sentence under the heading", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    expect(
      screen.getByText(
        /use your organisation account\. contigo never stores your password.*microsoft entra id/i,
      ),
    ).toBeInTheDocument();
  });

  it("renders the full-viewport two-column canvas", () => {
    const { container } = render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    expect(container.querySelector("main.signin-screen")).toBeInTheDocument();
  });
});
