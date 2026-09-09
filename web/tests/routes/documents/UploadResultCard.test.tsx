import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UploadResultCard from "../../../src/routes/documents/UploadResultCard";
import { getRejectionReasonCopy } from "../../../src/routes/documents/uploadPipeline";

// R-DOC-04: the "Not added" card for a rejected file (session-only, never a document row).
describe("UploadResultCard (Not added card, R-DOC-04)", () => {
  it("renders the filename, the 'Not added' tag and the full mapped rejection sentence", () => {
    const message = getRejectionReasonCopy("not_a_contract");
    render(<UploadResultCard fileName="recipe.pdf" message={message} onDismiss={vi.fn()} />);

    expect(screen.getByText("Not added")).toHaveClass("tag", "tag-outline");
    expect(screen.getByText("recipe.pdf")).toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
  });

  it("renders the no_readable_text sentence distinctly", () => {
    const message = getRejectionReasonCopy("no_readable_text");
    render(<UploadResultCard fileName="blurry-scan.png" message={message} onDismiss={vi.fn()} />);

    expect(screen.getByText(message)).toBeInTheDocument();
  });

  it("calls onDismiss when dismissed", async () => {
    const onDismiss = vi.fn();
    render(<UploadResultCard fileName="recipe.pdf" message="Not added: ..." onDismiss={onDismiss} />);

    await userEvent.click(screen.getByRole("button", { name: /dismiss/i }));

    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it("renders as a plain row, not a boxed .card (ADR-019 reserves .card for recommendation/provenance)", () => {
    const { container } = render(<UploadResultCard fileName="recipe.pdf" message="Not added: ..." onDismiss={vi.fn()} />);

    const root = container.querySelector(".upload-result-card");
    expect(root).not.toBeNull();
    expect(root).not.toHaveClass("card");
  });
});
