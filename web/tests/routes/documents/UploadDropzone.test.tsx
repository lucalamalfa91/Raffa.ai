import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UploadDropzone from "../../../src/routes/documents/UploadDropzone";

function pdfFile(name = "contract.pdf") {
  return new File(["%PDF-1.4"], name, { type: "application/pdf" });
}

// AC-1: "Dropzone (drag-and-drop + file picker), formats/size/sources strip."
describe("UploadDropzone", () => {
  it("renders the heading, subtitle and formats/size/sources strip verbatim from the prototype", () => {
    render(<UploadDropzone disabled={false} onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);

    expect(screen.getByText("Drop contracts here")).toBeInTheDocument();
    expect(
      screen.getByText("Each file becomes a processing job; you can leave this page while it runs."),
    ).toBeInTheDocument();
    expect(screen.getByText("PDF · DOCX · XLSX")).toBeInTheDocument();
    expect(screen.getByText("50 MB / file")).toBeInTheDocument();
    expect(screen.getByText("Local · SharePoint soon")).toBeInTheDocument();
  });

  it("opens the native file picker when 'Choose from computer' is clicked", async () => {
    render(<UploadDropzone disabled={false} onFilesSelected={vi.fn()} onUseSampleFile={vi.fn()} />);
    const input = screen.getByLabelText(/choose contract files from your computer/i) as HTMLInputElement;
    const clickSpy = vi.spyOn(input, "click");

    await userEvent.click(screen.getByRole("button", { name: "Choose from computer" }));

    expect(clickSpy).toHaveBeenCalledTimes(1);
  });

  it("reports the chosen file(s) via the hidden input's change event", () => {
    const onFilesSelected = vi.fn();
    render(<UploadDropzone disabled={false} onFilesSelected={onFilesSelected} onUseSampleFile={vi.fn()} />);
    const input = screen.getByLabelText(/choose contract files from your computer/i) as HTMLInputElement;
    const file = pdfFile();

    fireEvent.change(input, { target: { files: [file] } });

    expect(onFilesSelected).toHaveBeenCalledWith([file]);
  });

  it("accepts a dropped file and clears the drag-over highlight", () => {
    const onFilesSelected = vi.fn();
    const { container } = render(
      <UploadDropzone disabled={false} onFilesSelected={onFilesSelected} onUseSampleFile={vi.fn()} />,
    );
    const dropzone = container.querySelector(".upload-dropzone");
    expect(dropzone).not.toBeNull();
    const file = pdfFile();

    fireEvent.dragOver(dropzone!, { dataTransfer: { files: [file] } });
    expect(dropzone).toHaveClass("is-dragging");

    fireEvent.drop(dropzone!, { dataTransfer: { files: [file] } });

    expect(onFilesSelected).toHaveBeenCalledWith([file]);
    expect(dropzone).not.toHaveClass("is-dragging");
  });

  it("ignores drag/drop while disabled (an upload is already in flight)", () => {
    const onFilesSelected = vi.fn();
    const { container } = render(
      <UploadDropzone disabled={true} onFilesSelected={onFilesSelected} onUseSampleFile={vi.fn()} />,
    );
    const dropzone = container.querySelector(".upload-dropzone");

    fireEvent.dragOver(dropzone!, { dataTransfer: { files: [pdfFile()] } });
    fireEvent.drop(dropzone!, { dataTransfer: { files: [pdfFile()] } });

    expect(onFilesSelected).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Choose from computer" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Use sample file" })).toBeDisabled();
  });

  it("calls onUseSampleFile when 'Use sample file' is clicked", async () => {
    const onUseSampleFile = vi.fn();
    render(<UploadDropzone disabled={false} onFilesSelected={vi.fn()} onUseSampleFile={onUseSampleFile} />);

    await userEvent.click(screen.getByRole("button", { name: "Use sample file" }));

    expect(onUseSampleFile).toHaveBeenCalledTimes(1);
  });
});
