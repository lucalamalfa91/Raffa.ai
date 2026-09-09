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
    const button = screen.getByRole("button", { name: /redirecting to login\.microsoftonline\.com/i });
    expect(button).toBeDisabled();
  });

  it("renders the redirecting state up front when MSAL already has an interaction in flight", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={true} />);

    expect(screen.getByRole("button", { name: /redirecting to login\.microsoftonline\.com/i })).toBeDisabled();
    expect(screen.queryByRole("button", { name: /^continue with microsoft entra id$/i })).not.toBeInTheDocument();
  });

  it("renders the V2 three-line north star and the four answers", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    // contigo-v2/markup.html: three stacked lines, the middle one in accent.
    expect(screen.getByText("Your contracts.")).toBeInTheDocument();
    expect(screen.getByText("Your savings.")).toHaveClass("signin-accent");
    expect(screen.getByText("Nothing missed.")).toBeInTheDocument();

    expect(screen.getByText("One platform, four answers")).toBeInTheDocument();
    expect(screen.getByText("Contract Intelligence")).toBeInTheDocument();
    expect(screen.getByText("What you bought")).toBeInTheDocument();
    expect(screen.getByText("Renewal Intelligence")).toBeInTheDocument();
    expect(screen.getByText("When to act")).toBeInTheDocument();
    expect(screen.getByText("Savings Intelligence")).toBeInTheDocument();
    expect(screen.getByText("Where to save")).toBeInTheDocument();
    expect(screen.getByText("Quote Check")).toBeInTheDocument();
    expect(screen.getByText("Before you buy")).toBeInTheDocument();

    expect(screen.getByText("Contract intelligence for procurement teams.")).toBeInTheDocument();
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

    // contigo-v2/markup.html, verbatim.
    expect(
      screen.getByText(/your organisation account\. contigo never stores a password\./i),
    ).toBeInTheDocument();
  });

  it("renders the full-viewport two-column canvas", () => {
    const { container } = render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    expect(container.querySelector("main.signin-screen")).toBeInTheDocument();
  });
});
