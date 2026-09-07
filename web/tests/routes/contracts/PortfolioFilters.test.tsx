import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import PortfolioFilters from "../../../src/routes/contracts/PortfolioFilters";
import { EMPTY_PORTFOLIO_FILTERS } from "../../../src/routes/contracts/portfolioFilterState";

// AC-1 "Filter chips (Supplier, Category, Renewal period, Spend, Status, Risk, Auto-renewal)".

describe("PortfolioFilters", () => {
  it("renders all seven AC-1 chips", () => {
    render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={vi.fn()} />);

    expect(screen.getByLabelText("Supplier")).toBeInTheDocument();
    expect(screen.getByLabelText("Category")).toBeInTheDocument();
    expect(screen.getByLabelText("Renewal period")).toBeInTheDocument();
    expect(screen.getByLabelText("Minimum annual spend")).toBeInTheDocument();
    expect(screen.getByLabelText("Maximum annual spend")).toBeInTheDocument();
    expect(screen.getByLabelText("Status")).toBeInTheDocument();
    expect(screen.getByLabelText("Risk")).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Auto-renewal" })).toBeInTheDocument();
  });

  it("Category is a disabled placeholder, not a fabricated working filter (no Category concept on the backend yet)", () => {
    render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={vi.fn()} />);

    expect(screen.getByLabelText("Category")).toBeDisabled();
  });

  it("typing a Supplier id calls onChange with just that field patched", () => {
    const onChange = vi.fn();
    render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Supplier"), { target: { value: "supplier-1" } });

    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PORTFOLIO_FILTERS, supplierId: "supplier-1" });
  });

  it("choosing a Risk option calls onChange with the real backend enum value", () => {
    const onChange = vi.fn();
    render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Risk"), { target: { value: "High" } });

    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PORTFOLIO_FILTERS, risk: "High" });
  });

  it("choosing 'Any risk' clears the risk filter", () => {
    const onChange = vi.fn();
    render(<PortfolioFilters filters={{ ...EMPTY_PORTFOLIO_FILTERS, risk: "High" }} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Risk"), { target: { value: "" } });

    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PORTFOLIO_FILTERS, risk: "" });
  });

  it("choosing a Renewal period preset patches renewalWithinDays with a number", () => {
    const onChange = vi.fn();
    render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Renewal period"), { target: { value: "120" } });

    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PORTFOLIO_FILTERS, renewalWithinDays: 120 });
  });

  it("entering Min/Max spend patches the corresponding field as free text", () => {
    const onChange = vi.fn();
    render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("Minimum annual spend"), { target: { value: "1000" } });
    expect(onChange).toHaveBeenLastCalledWith({ ...EMPTY_PORTFOLIO_FILTERS, minAnnualSpend: "1000" });

    fireEvent.change(screen.getByLabelText("Maximum annual spend"), { target: { value: "500000" } });
    expect(onChange).toHaveBeenLastCalledWith({ ...EMPTY_PORTFOLIO_FILTERS, maxAnnualSpend: "500000" });
  });

  it("clicking an Auto-renewal segment option patches autoRenewal", () => {
    const onChange = vi.fn();
    render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={onChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Yes" }));

    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PORTFOLIO_FILTERS, autoRenewal: "yes" });
  });

  it("'Clear filters' is disabled when nothing is active, and resets everything when clicked", () => {
    const onChange = vi.fn();
    const { rerender } = render(<PortfolioFilters filters={EMPTY_PORTFOLIO_FILTERS} onChange={onChange} />);

    expect(screen.getByRole("button", { name: "Clear filters" })).toBeDisabled();

    const activeFilters = { ...EMPTY_PORTFOLIO_FILTERS, status: "active" };
    rerender(<PortfolioFilters filters={activeFilters} onChange={onChange} />);

    const clearButton = screen.getByRole("button", { name: "Clear filters" });
    expect(clearButton).not.toBeDisabled();

    fireEvent.click(clearButton);
    expect(onChange).toHaveBeenCalledWith(EMPTY_PORTFOLIO_FILTERS);
  });
});
