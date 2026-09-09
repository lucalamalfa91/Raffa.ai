import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UploadDropzone from "../../../src/routes/documents/UploadDropzone";

function pdfFile(name = "contract.pdf") {
  return new File(["%PDF-1.4"], name, { type: "application/pdf" });
}

// R-DOC-01/02: multi-file, widened accept (PDF/DOCX/XLSX/PNG/JPG); task's own "accept widened, up
// to 20 files" -- the size/count ceilings themselves are proven in uploadPipeline.test.ts.
describe("UploadDropzone", () => {
  it("onboarding variant renders 'Upload contracts' and 'Use the sample MSA'", () => {
    render(<UploadDropzone variant="onboarding" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByRole("button", { name: "Upload contracts" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Use the sample MSA" })).toBeInTheDocument();
    expect(screen.getByText("or drop files anywhere in this box")).toBeInTheDocument();
  });

  it("onboarding variant renders the widened formats/size strip", () => {
    render(<UploadDropzone variant="onboarding" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByText("PDF · DOCX · XLSX · PNG · JPG")).toBeInTheDocument();
    expect(screen.getByText("50 MB / file")).toBeInTheDocument();
    expect(screen.getByText("20 files at once")).toBeInTheDocument();
  });

  it("list variant renders the compact 'Sample MSA' label and inline hint, no strip", () => {
    render(<UploadDropzone variant="list" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByRole("button", { name: "Sample MSA" })).toBeInTheDocument();
    expect(screen.getByText(/drop PDF · DOCX · XLSX · PNG · JPG here/)).toBeInTheDocument();
    expect(screen.queryByText("50 MB / file")).not.toBeInTheDocument();
  });

  it("the file input accepts the widened, D7 (PNG/JPG) extension list", () => {
    render(<UploadDropzone variant="list" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    const input = screen.getByLabelText(/choose contract files from your computer/i) as HTMLInputElement;
    expect(input).toHaveAttribute("multiple");
    expect(input.accept).toBe(".pdf,.docx,.xlsx,.png,.jpg,.jpeg");
  });

  it("opens the native file picker when 'Upload contracts' is clicked", async () => {
    render(<UploadDropzone variant="list" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);
    const input = screen.getByLabelText(/choose contract files from your computer/i) as HTMLInputElement;
    const clickSpy = vi.spyOn(input, "click");

    await userEvent.click(screen.getByRole("button", { name: "Upload contracts" }));

    expect(clickSpy).toHaveBeenCalledTimes(1);
  });

  it("reports every chosen file via the hidden input's change event (multi-file)", () => {
    const onFilesSelected = vi.fn();
    render(<UploadDropzone variant="list" onFilesSelected={onFilesSelected} onUseSampleFile={vi.fn()} />);
    const input = screen.getByLabelText(/choose contract files from your computer/i) as HTMLInputElement;
    const files = [pdfFile("A.pdf"), pdfFile("B.pdf"), pdfFile("C.pdf")];

    fireEvent.change(input, { target: { files } });

    expect(onFilesSelected).toHaveBeenCalledWith(files);
  });

  it("accepts dropped files and clears the drag-over highlight", () => {
    const onFilesSelected = vi.fn();
    const { container } = render(<UploadDropzone variant="list" onFilesSelected={onFilesSelected} onUseSampleFile={vi.fn()} />);
    const dropzone = container.querySelector(".upload-dropzone");
    expect(dropzone).not.toBeNull();
    const files = [pdfFile("A.pdf"), pdfFile("B.pdf")];

    fireEvent.dragOver(dropzone!, { dataTransfer: { files } });
    expect(dropzone).toHaveClass("is-dragging");

    fireEvent.drop(dropzone!, { dataTransfer: { files } });

    expect(onFilesSelected).toHaveBeenCalledWith(files);
    expect(dropzone).not.toHaveClass("is-dragging");
  });

  it("calls onUseSampleFile when the sample button is clicked", async () => {
    const onUseSampleFile = vi.fn();
    render(<UploadDropzone variant="onboarding" onFilesSelected={vi.fn()} onUseSampleFile={onUseSampleFile} />);

    await userEvent.click(screen.getByRole("button", { name: "Use the sample MSA" }));

    expect(onUseSampleFile).toHaveBeenCalledTimes(1);
  });
});
