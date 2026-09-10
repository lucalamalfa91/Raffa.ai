import { describe, expect, it } from "vitest";
import {
  buildSamplePdf,
  createSampleDocumentFile,
  getSampleDocument,
  SAMPLE_DOCUMENTS,
} from "../../../src/routes/documents/sampleDocument";

/** Mirrors the fixture gateway's own lightweight scanner (`FixturePdfTextScanner`): count
 * `/Type /Page` objects, pair them with the `BT ... ET` streams in file order, join each stream's
 * literal strings with spaces. */
function extractPageTexts(pdf: string): string[] {
  const pageCount = (pdf.match(/(?<![A-Za-z])\/Type\s*\/Page(?![A-Za-z])/g) ?? []).length;
  const streams = [...pdf.matchAll(/BT([\s\S]*?)ET/g)].map((match) =>
    [...match[1].matchAll(/\((?<text>(?:[^()\\]|\\.)*)\)/g)]
      .map((literal) => literal.groups!.text.replace(/\\([()\\])/g, "$1"))
      .join(" "),
  );
  expect(streams).toHaveLength(pageCount);
  return streams;
}

describe("SAMPLE_DOCUMENTS", () => {
  it("ships two samples with two different suppliers: a clean MSA and one that needs review", () => {
    expect(SAMPLE_DOCUMENTS.map((sample) => sample.key)).toEqual(["clean", "needs-review"]);
    expect(SAMPLE_DOCUMENTS.map((sample) => sample.supplier)).toEqual(["Northwind Traders SA", "Fabrikam Software GmbH"]);
    expect(new Set(SAMPLE_DOCUMENTS.map((sample) => sample.fileName)).size).toBe(2);
  });

  it("every sample is a readable, classifiable contract: 'MASTER SERVICES AGREEMENT' and well over the 200 readable characters the admission gate requires", () => {
    for (const sample of SAMPLE_DOCUMENTS) {
      const pages = extractPageTexts(buildSamplePdf(sample.pages));
      expect(pages).toHaveLength(sample.pages.length);
      expect(pages[0]).toContain("MASTER SERVICES AGREEMENT");
      expect(pages.join(" ").replace(/\s/g, "").length).toBeGreaterThan(200);
    }
  });

  it("the clean sample states every commercial term once, with the supplier role labelled", () => {
    const text = getSampleDocument("clean").pages.join(" ");

    expect(text).toContain('Northwind Traders SA ("Supplier")');
    expect(text).toContain("EUR 48,000");
    expect(text).toContain("renews automatically");
    expect(text).not.toMatch(/shall not automatically renew/);
  });

  it("the needs-review sample is genuinely ambiguous: unlabelled parties, two annual fees, a self-contradicting renewal clause", () => {
    const text = getSampleDocument("needs-review").pages.join(" ");

    expect(text).toContain("between Contigo Demo AG and Fabrikam Software GmbH");
    expect(text).not.toContain('("Supplier")');
    expect(text).toContain("EUR 36,000");
    expect(text).toContain("EUR 39,600");
    expect(text).toContain("renews automatically");
    expect(text).toContain("shall not automatically renew");
  });
});

describe("buildSamplePdf", () => {
  it("emits one page object and one text stream per page, with the page text recoverable exactly", () => {
    const pdf = buildSamplePdf(["First page (with parentheses) and a back\\slash.", "Second page."]);

    expect(pdf.startsWith("%PDF-1.4")).toBe(true);
    expect(pdf).toContain("/Count 2");
    const pages = extractPageTexts(pdf);
    expect(pages).toEqual(["First page (with parentheses) and a back\\slash.", "Second page."]);
  });

  it("is a structurally valid PDF: every xref entry points at its object and startxref points at the xref table", () => {
    const pdf = buildSamplePdf(["Page one.", "Page two."]);

    const startXref = Number(/startxref\n(\d+)\n%%EOF/.exec(pdf)![1]);
    expect(pdf.slice(startXref, startXref + 4)).toBe("xref");

    const entries = [...pdf.matchAll(/^(\d{10}) 00000 n \r?$/gm)].map((match) => Number(match[1]));
    const objectCount = Number(/xref\n0 (\d+)\n/.exec(pdf)![1]) - 1;
    expect(entries).toHaveLength(objectCount);
    entries.forEach((offset, index) => {
      expect(pdf.slice(offset, offset + `${index + 1} 0 obj`.length)).toBe(`${index + 1} 0 obj`);
    });
    expect(pdf).toContain(`/Size ${objectCount + 1}`);
  });

  it("stays within ASCII so byte offsets equal character offsets once the file is UTF-8 encoded", () => {
    for (const sample of SAMPLE_DOCUMENTS) {
      const pdf = buildSamplePdf(sample.pages);
      for (const char of pdf) {
        expect(char.charCodeAt(0)).toBeLessThan(128);
      }
    }
  });
});

describe("createSampleDocumentFile", () => {
  it("builds a real PDF File for the requested sample, accepted by the dropzone's own filter", () => {
    const clean = createSampleDocumentFile("clean");
    const ambiguous = createSampleDocumentFile("needs-review");

    expect(clean).toBeInstanceOf(File);
    expect(clean.name).toBe("contigo-sample-northwind-msa.pdf");
    expect(ambiguous.name).toBe("contigo-sample-fabrikam-msa.pdf");
    expect(clean.type).toBe("application/pdf");
    expect(clean.size).toBeGreaterThan(0);
  });

  it("defaults to the clean sample and returns a fresh File instance on every call", () => {
    const first = createSampleDocumentFile();
    const second = createSampleDocumentFile();

    expect(first.name).toBe("contigo-sample-northwind-msa.pdf");
    expect(first).not.toBe(second);
  });
});
