import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PortfolioFilterControl from "../../../src/routes/contracts/PortfolioFilterControl";

/**
 * Task E24/F01/US02/T01 (story us-02-portfolio-category-web; closes NW-23). The control in
 * isolation -- no fetch, no router; `onApply`/`onClear` are the only way it ever reports a value.
 * See `PortfolioRoute.test.tsx` for the whole route's own `?category=`/`getPortfolio` round trip.
 */
describe("PortfolioFilterControl", () => {
  it("AC-1: submitting (Apply) reports the typed draft via onApply", async () => {
    const user = userEvent.setup();
    const onApply = vi.fn();
    render(<PortfolioFilterControl category="" onApply={onApply} onClear={vi.fn()} />);

    // Exact match: the form's own `aria-label` ("Filter portfolio by supplier category") also
    // contains the word "category", so a substring/regex query would match both it and the input.
    await user.type(screen.getByLabelText("Category"), "Software");
    await user.click(screen.getByRole("button", { name: /^apply$/i }));

    expect(onApply).toHaveBeenCalledTimes(1);
    expect(onApply).toHaveBeenCalledWith("Software");
  });

  it("submitting via Enter (no explicit click) also reports the draft", async () => {
    const user = userEvent.setup();
    const onApply = vi.fn();
    render(<PortfolioFilterControl category="" onApply={onApply} onClear={vi.fn()} />);

    await user.type(screen.getByLabelText("Category"), "Logistics{Enter}");

    expect(onApply).toHaveBeenCalledWith("Logistics");
  });

  it("hides \"Clear filter\" while no category is applied", () => {
    render(<PortfolioFilterControl category="" onApply={vi.fn()} onClear={vi.fn()} />);
    expect(screen.queryByRole("button", { name: /clear filter/i })).not.toBeInTheDocument();
  });

  it("AC-3: Clear filter calls onClear once a category is applied, and resets the draft", async () => {
    const user = userEvent.setup();
    const onClear = vi.fn();
    render(<PortfolioFilterControl category="Software" onApply={vi.fn()} onClear={onClear} />);

    const input = screen.getByLabelText("Category") as HTMLInputElement;
    expect(input.value).toBe("Software");

    await user.click(screen.getByRole("button", { name: /clear filter/i }));

    expect(onClear).toHaveBeenCalledTimes(1);
    expect(input.value).toBe("");
  });

  it("resyncs the draft when the committed category changes from elsewhere (e.g. browser back/forward)", () => {
    const { rerender } = render(<PortfolioFilterControl category="Software" onApply={vi.fn()} onClear={vi.fn()} />);
    rerender(<PortfolioFilterControl category="Logistics" onApply={vi.fn()} onClear={vi.fn()} />);
    expect((screen.getByLabelText("Category") as HTMLInputElement).value).toBe("Logistics");
  });
});
