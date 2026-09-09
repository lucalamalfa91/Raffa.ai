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
  it("onboarding variant renders 'Upload contracts' and both sample MSAs", () => {
    render(<UploadDropzone variant="onboarding" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByRole("button", { name: "Upload contracts" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sample MSA · clean" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sample MSA · needs review" })).toBeInTheDocument();
    expect(screen.getByText("Or try a sample:")).toBeInTheDocument();
    expect(screen.getByText("or drop files anywhere in this box")).toBeInTheDocument();
  });

  it("onboarding variant renders the widened formats/size strip", () => {
    render(<UploadDropzone variant="onboarding" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByText("PDF · DOCX · XLSX · PNG · JPG")).toBeInTheDocument();
    expect(screen.getByText("50 MB / file")).toBeInTheDocument();
    expect(screen.getByText("20 files at once")).toBeInTheDocument();
  });

  it("list variant renders the same two sample buttons and the inline hint, no strip", () => {
    render(<UploadDropzone variant="list" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByRole("button", { name: "Sample MSA · clean" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sample MSA · needs review" })).toBeInTheDocument();
    expect(screen.getByText(/drop PDF · DOCX · XLSX · PNG · JPG here/)).toBeInTheDocument();
    expect(screen.queryByText("50 MB / file")).not.toBeInTheDocument();
    expect(screen.queryByText("Or try a sample:")).not.toBeInTheDocument();
  });

  it("names the supplier and the expected outcome on each sample button's title", () => {
    render(<UploadDropzone variant="list" onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByRole("button", { name: "Sample MSA · clean" })).toHaveAttribute("title", expect.stringContaining("Northwind Traders SA"));
    expect(screen.getByRole("button", { name: "Sample MSA · needs review" })).toHaveAttribute(
      "title",
      expect.stringContaining("Fabrikam Software GmbH"),
    );
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

  it("calls onUseSampleFile with the sample's own key when either sample button is clicked", async () => {
    const onUseSampleFile = vi.fn();
    render(<UploadDropzone variant="onboarding" onFilesSelected={vi.fn()} onUseSampleFile={onUseSampleFile} />);

    await userEvent.click(screen.getByRole("button", { name: "Sample MSA · clean" }));
    await userEvent.click(screen.getByRole("button", { name: "Sample MSA · needs review" }));

    expect(onUseSampleFile).toHaveBeenNthCalledWith(1, "clean");
    expect(onUseSampleFile).toHaveBeenNthCalledWith(2, "needs-review");
  });
});
