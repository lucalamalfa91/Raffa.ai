import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import ProcessingPipeline from "../../../src/routes/documents/ProcessingPipeline";

// R-DOC-09: the real stage (and its percent-complete bar) comes from the API, not a client timer.
describe("ProcessingPipeline", () => {
  it("renders a progressbar sized to the stage's own position among the six real stages", () => {
    render(<ProcessingPipeline stage="Extracting facts" />);

    const bar = screen.getByRole("progressbar");
    expect(bar).toHaveAttribute("aria-valuenow", "83"); // 5/6
  });

  it("renders 0% before any stage is known (null)", () => {
    render(<ProcessingPipeline stage={null} />);

    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "0");
  });

  it("renders 100% once at the last stage", () => {
    render(<ProcessingPipeline stage="Validating schema" />);

    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "100");
  });
});
