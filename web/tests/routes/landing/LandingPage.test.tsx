import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import LandingPage from "../../../src/routes/landing/LandingPage";
import { computeRenewalCost, formatAmount, formatCompact } from "../../../src/routes/landing/renewalCost";

describe("computeRenewalCost", () => {
  it("compounds the uplift from the next renewal on (100,000 at 2% over 3 years)", () => {
    const cost = computeRenewalCost(100_000, 2, 3);
    expect(cost.rows.map((row) => Number(row.annual.toFixed(2)))).toEqual([102_000, 104_040, 106_120.8]);
    expect(cost.extra).toBeCloseTo(12_160.8, 6);
    expect(cost.total).toBeCloseTo(312_160.8, 6);
    expect(cost.finalIncrease).toBeCloseTo(6.1208, 4);
  });

  it("refuses values outside the calculator's range", () => {
    expect(() => computeRenewalCost(-1, 2, 3)).toThrow();
    expect(() => computeRenewalCost(100, 2, 11)).toThrow();
  });

  it("formats amounts with thousands separators and a compact form", () => {
    expect(formatAmount(12_160.8)).toBe("12,161");
    expect(formatAmount(12_160.8, 2)).toBe("12,160.80");
    expect(formatCompact(12_500_000)).toBe("12.50m");
  });
});

describe("LandingPage", () => {
  it("shows the default calculator result", () => {
    render(<LandingPage submitReview={vi.fn()} />);
    expect(screen.getByTestId("landing-extra-cost")).toHaveTextContent("CHF 12,161");
  });

  it("recomputes when the annual cost changes", () => {
    render(<LandingPage submitReview={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Your current annual software cost"), { target: { value: "200000" } });
    expect(screen.getByTestId("landing-extra-cost")).toHaveTextContent("CHF 24,322");
  });

  async function fillForm() {
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Your name"), "Alex Taylor");
    await user.type(screen.getByLabelText("Work email"), "alex@company.com");
    await user.type(screen.getByLabelText("Company"), "Acme");
    await user.selectOptions(screen.getByLabelText("Next renewal"), "1–3 months");
    await user.click(screen.getByRole("checkbox"));
    await user.click(screen.getByRole("button", { name: /find your savings/i }));
  }

  it("sends the review request and confirms it", async () => {
    const submitReview = vi.fn().mockResolvedValue({ ok: true });
    render(<LandingPage submitReview={submitReview} />);

    await fillForm();

    expect(submitReview).toHaveBeenCalledWith({
      name: "Alex Taylor",
      email: "alex@company.com",
      company: "Acme",
      renewal: "1–3 months",
      consent: true,
      website: "",
    });
    expect(await screen.findByText("REQUEST RECEIVED")).toBeInTheDocument();
  });

  it("shows the server's message when the request is refused", async () => {
    const submitReview = vi.fn().mockResolvedValue({ ok: false, error: "Please enter a valid work email." });
    render(<LandingPage submitReview={submitReview} />);

    await fillForm();

    expect(await screen.findByRole("alert")).toHaveTextContent("Please enter a valid work email.");
  });
});
