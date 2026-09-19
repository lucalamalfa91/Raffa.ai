import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import TablePager from "./TablePager";

describe("TablePager", () => {
  it("hides when the list fits on one page", () => {
    const { container } = render(<TablePager page={1} totalItems={10} onPageChange={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("offers previous, page numbers, and next once there is a second page", async () => {
    const onPageChange = vi.fn();
    render(<TablePager page={1} totalItems={12} onPageChange={onPageChange} />);

    expect(screen.getByRole("navigation", { name: "Table pages" })).toBeInTheDocument();
    expect(screen.getByText("1–10 of 12")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Previous" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Page 1" })).toHaveAttribute("aria-current", "page");

    await userEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(onPageChange).toHaveBeenCalledWith(2);

    await userEvent.click(screen.getByRole("button", { name: "Page 2" }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it("disables Next on the last page", () => {
    render(<TablePager page={2} totalItems={12} onPageChange={vi.fn()} />);

    expect(screen.getByText("11–12 of 12")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Previous" })).toBeEnabled();
  });
});
