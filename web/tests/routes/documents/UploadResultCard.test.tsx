import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UploadResultCard from "../../../src/routes/documents/UploadResultCard";
import { getResultCardContent, type UploadOutcome } from "../../../src/routes/documents/uploadPipeline";

// Task-01's own named "Tests required" row: "unit | outcome cards by status".
// AC-3: "Result card per outcome (needs_review / completed / failed)".
describe("UploadResultCard (AC-3: outcome cards by status)", () => {
  it.each<{ outcome: UploadOutcome; tagClass: string; tagText: string; ctaText: string }>([
    { outcome: "completed", tagClass: "tag-neutral", tagText: "Completed", ctaText: "Open Contract 360" },
    { outcome: "needs_review", tagClass: "tag-outline", tagText: "Needs review", ctaText: "Review extraction" },
    { outcome: "failed", tagClass: "tag-accent", tagText: "Failed", ctaText: "Retry upload" },
  ])("renders the $outcome card with its own tag, message and CTA", ({ outcome, tagClass, tagText, ctaText }) => {
    const { message } = getResultCardContent(outcome, "Acme_MSA.pdf");

    render(
      <UploadResultCard
        fileName="Acme_MSA.pdf"
        outcome={outcome}
        message={message}
        onPrimaryAction={vi.fn()}
        onUploadAnother={vi.fn()}
      />,
    );

    const tag = screen.getByText(tagText);
    expect(tag).toHaveClass("tag", tagClass);
    expect(screen.getByText("Acme_MSA.pdf")).toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: ctaText })).toBeInTheDocument();
  });

  it("calls onPrimaryAction when the outcome-specific CTA is clicked", async () => {
    const onPrimaryAction = vi.fn();
    render(
      <UploadResultCard
        fileName="Acme_MSA.pdf"
        outcome="completed"
        message="done"
        onPrimaryAction={onPrimaryAction}
        onUploadAnother={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Open Contract 360" }));

    expect(onPrimaryAction).toHaveBeenCalledTimes(1);
  });

  it("always offers 'Upload another', independent of outcome", async () => {
    const onUploadAnother = vi.fn();
    render(
      <UploadResultCard
        fileName="Acme_MSA.pdf"
        outcome="failed"
        message="failed"
        onPrimaryAction={vi.fn()}
        onUploadAnother={onUploadAnother}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Upload another" }));

    expect(onUploadAnother).toHaveBeenCalledTimes(1);
  });
});
