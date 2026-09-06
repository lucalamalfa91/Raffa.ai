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
    expect(screen.getByText("Intelligence")).toBeInTheDocument();
    expect(screen.getByText("Quote Check")).toBeInTheDocument();
  });

  it("tells a not-yet-provisioned user to ask their Workspace Admin", () => {
    render(<SignInScreen onContinue={vi.fn()} interactionInFlight={false} />);

    expect(screen.getByText(/ask your workspace admin for an invitation/i)).toBeInTheDocument();
  });
});
