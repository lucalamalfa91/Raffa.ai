import { describe, expect, it } from "vitest";
import { createSampleDocumentFile, SAMPLE_DOCUMENT_FILE_NAME } from "../../../src/routes/documents/sampleDocument";

describe("createSampleDocumentFile", () => {
  it("builds a real File with a PDF name/type accepted by the dropzone's own accept filter", () => {
    const file = createSampleDocumentFile();

    expect(file).toBeInstanceOf(File);
    expect(file.name).toBe(SAMPLE_DOCUMENT_FILE_NAME);
    expect(file.name.endsWith(".pdf")).toBe(true);
    expect(file.type).toBe("application/pdf");
    expect(file.size).toBeGreaterThan(0);
  });

  it("returns a fresh File instance on every call", () => {
    const first = createSampleDocumentFile();
    const second = createSampleDocumentFile();

    expect(first).not.toBe(second);
  });
});
