import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import ProcessingPipeline from "../../../src/routes/documents/ProcessingPipeline";
import { PIPELINE_STAGE_LABELS } from "../../../src/routes/documents/uploadPipeline";

// AC-2: "6-stage processing pipeline (current pulsing)".
describe("ProcessingPipeline", () => {
  it("renders all 6 stage labels, in order", () => {
    const { container } = render(<ProcessingPipeline currentStepIndex={0} />);

    PIPELINE_STAGE_LABELS.forEach((label) => {
      expect(screen.getByText(label)).toBeInTheDocument();
    });
    expect(container.querySelectorAll(".pipeline-stage")).toHaveLength(6);
  });

  it("marks exactly the current stage as pulsing-current, earlier ones done, later ones pending", () => {
    render(<ProcessingPipeline currentStepIndex={3} />);

    PIPELINE_STAGE_LABELS.forEach((label, index) => {
      const row = screen.getByText(label).closest(".pipeline-stage");
      expect(row).not.toBeNull();
      const expectedState = index < 3 ? "done" : index === 3 ? "current" : "pending";
      expect(row).toHaveClass(`pipeline-stage--${expectedState}`);
    });
  });

  it("announces stage changes to assistive tech (role=status, aria-live=polite)", () => {
    render(<ProcessingPipeline currentStepIndex={0} />);

    const region = screen.getByRole("status");
    expect(region).toHaveAttribute("aria-live", "polite");
  });
});
