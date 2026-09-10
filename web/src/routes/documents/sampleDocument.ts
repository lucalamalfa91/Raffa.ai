/**
 * The two "Sample MSA" files (`UploadDropzone.tsx`'s sample buttons, both dropzone variants). This
 * repo ships no real sample contract asset, so each sample is a small PDF built in the browser and
 * sent through the exact same `apiClient.uploadDocument()` call (via `useDocumentsList.ts#uploadFiles`)
 * a real drag-and-drop/file-picker upload uses -- a real end-to-end request against the deployed
 * API, not a client-only mock.
 *
 * **Two samples, two suppliers, two honest outcomes.** The outcome is whatever the deployed
 * pipeline really returns; the texts are written so that outcome is a *meaningful* one:
 *
 * - `clean` -- an MSA with Northwind Traders SA, every commercial term stated once, plainly, with
 *   the supplier role labelled. A reader (model or fixture) finds every field with high confidence.
 * - `needs-review` -- an MSA with Fabrikam Software GmbH whose text is genuinely ambiguous: the
 *   parties are named without saying which one supplies, the body and Schedule 1 disagree on the
 *   annual fee, and the renewal clause both affirms and denies automatic renewal. Exactly those
 *   fields deserve a low confidence and a human look, with real evidence (page, quoted span).
 *
 * **It has to be a real PDF, not a placeholder.** On Azure every PDF goes through Document
 * Intelligence (ADR-017, amended 2026-09-09), which parses the file for real: so `buildSamplePdf`
 * emits a structurally valid PDF -- catalog, page tree, one `/Type /Page` object with its own
 * uncompressed `BT ... Tj ... ET` content stream per page, a shared Helvetica font, a correct
 * cross-reference table with byte offsets, trailer and `startxref`. The same shape also stays
 * readable by the fixture gateway's own lightweight scanner (CI, local dev), which pairs page objects
 * with text streams in file order and joins a stream's literals with spaces. Text is kept to ASCII so
 * byte offsets equal character offsets, and every `(`, `)` and `\` is escaped as the PDF string
 * syntax requires.
 */

export type SampleDocumentKey = "clean" | "needs-review";

export interface SampleDocumentDefinition {
  key: SampleDocumentKey;
  /** Button label, identical in both dropzone variants. */
  label: string;
  /** One sentence for the button's `title`: who the supplier is and what the text is like. */
  description: string;
  fileName: string;
  supplier: string;
  /** One entry per PDF page; paragraphs inside a page are joined with a blank line. */
  pages: readonly string[];
}

export const SAMPLE_DOCUMENTS: readonly SampleDocumentDefinition[] = [
  {
    key: "clean",
    label: "Sample MSA · clean",
    description: "Northwind Traders SA — every term stated once, plainly, with the supplier role labelled.",
    fileName: "raffa-sample-northwind-msa.pdf",
    supplier: "Northwind Traders SA",
    pages: [
      [
        "MASTER SERVICES AGREEMENT",
        'This Master Services Agreement is entered into between Raffa Demo AG ("Customer") and Northwind Traders SA ("Supplier"), effective 2026-01-01.',
        "1. Scope. This Agreement governs every Order Form the parties execute under it for the Supplier's cloud procurement platform and related support services.",
        "2. Fees. The annual subscription fee is EUR 48,000, invoiced yearly in advance. The total contract value for the initial term is EUR 144,000. All invoices are payable within thirty (30) days of receipt.",
      ].join("\n\n"),
      [
        "3. Term and renewal. The initial term is thirty-six (36) months from the effective date. Thereafter this Agreement renews automatically for successive twelve (12) month terms unless either party gives ninety (90) days written notice before the end of the then-current term. Price increases at renewal are capped at four percent.",
        "4. Termination. Either party may terminate this Agreement for material breach not cured within thirty (30) days of written notice.",
        "5. Governing law. This Agreement is governed by the laws of Switzerland, and the courts of Zurich have exclusive jurisdiction.",
      ].join("\n\n"),
    ],
  },
  {
    key: "needs-review",
    label: "Sample MSA · needs review",
    description: "Fabrikam Software GmbH — unlabelled parties, two annual fees and a self-contradicting renewal clause.",
    fileName: "raffa-sample-fabrikam-msa.pdf",
    supplier: "Fabrikam Software GmbH",
    pages: [
      [
        "MASTER SERVICES AGREEMENT",
        "This Master Services Agreement is made between Raffa Demo AG and Fabrikam Software GmbH, effective 1 February 2026.",
        "1. Scope. Fabrikam provides its analytics platform and support services to Raffa Demo AG under the Order Forms executed from time to time.",
        "2. Fees. The annual subscription fee is EUR 36,000, invoiced quarterly in arrears; Schedule 1, however, lists an annual fee of EUR 39,600 after the agreed uplift. Invoices are payable within forty-five (45) days.",
      ].join("\n\n"),
      [
        "3. Term and renewal. The initial term is twenty-four (24) months. This Agreement renews automatically for successive twelve (12) month periods; notwithstanding the foregoing, the Customer may elect in writing that this Agreement shall not automatically renew.",
        "4. Notice. Either party may give sixty (60) days written notice before the end of the current term.",
        "5. Governing law. This Agreement is governed by the laws of Germany.",
      ].join("\n\n"),
    ],
  },
];

export function getSampleDocument(key: SampleDocumentKey): SampleDocumentDefinition {
  const found = SAMPLE_DOCUMENTS.find((sample) => sample.key === key);
  if (found === undefined) {
    throw new Error(`Unknown sample document: ${key}`);
  }
  return found;
}

/** Characters per rendered line -- keeps a page's text inside the page box at 10.5pt Helvetica. */
const MAX_LINE_LENGTH = 92;

/** Escapes the three characters PDF literal strings reserve (`\`, `(`, `)`). */
function escapePdfString(text: string): string {
  return text.replace(/\\/g, "\\\\").replace(/\(/g, "\\(").replace(/\)/g, "\\)");
}

/** Word-wraps one paragraph; a reader joins a stream's literals with single spaces, so wrapping
 * on spaces reproduces the paragraph text exactly. */
function wrapParagraph(paragraph: string): string[] {
  const lines: string[] = [];
  let current = "";
  for (const word of paragraph.split(/\s+/).filter((token) => token.length > 0)) {
    const candidate = current === "" ? word : `${current} ${word}`;
    if (candidate.length > MAX_LINE_LENGTH && current !== "") {
      lines.push(current);
      current = word;
    } else {
      current = candidate;
    }
  }
  if (current !== "") lines.push(current);
  return lines;
}

function buildContentStream(pageText: string): string {
  const lines = pageText
    .split(/\n{2,}/)
    .flatMap((paragraph) => [...wrapParagraph(paragraph), ""])
    .slice(0, -1);
  const showOperators = lines.map((line) => `(${escapePdfString(line)}) Tj T*`).join("\n");
  return `BT /F1 10.5 Tf 14 TL 54 740 Td\n${showOperators}\nET`;
}

/** Every character must be ASCII: the file is UTF-8 encoded on upload, and the cross-reference
 * table below records *byte* offsets, which equal character offsets only for ASCII. */
function assertAscii(text: string): void {
  for (let index = 0; index < text.length; index += 1) {
    if (text.charCodeAt(index) > 0x7f) {
      throw new Error(`Sample PDF text must be ASCII; found U+${text.charCodeAt(index).toString(16)} at ${index}.`);
    }
  }
}

/**
 * A minimal but structurally valid multi-page PDF (see the header comment): one catalog, one page
 * tree, per page a `/Type /Page` object and its own uncompressed content stream, one shared Helvetica
 * font, then a cross-reference table with the byte offset of every object, the trailer and
 * `startxref` -- what a real parser (Document Intelligence, a browser preview) needs to open the file.
 */
export function buildSamplePdf(pages: readonly string[]): string {
  const pageCount = pages.length;
  const fontObjectNumber = 3 + pageCount * 2;
  const kids = pages.map((_, index) => `${3 + index * 2} 0 R`).join(" ");

  const objectBodies: string[] = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    `<< /Type /Pages /Kids [${kids}] /Count ${pageCount} >>`,
  ];

  pages.forEach((pageText, index) => {
    const contentObjectNumber = 3 + index * 2 + 1;
    const stream = buildContentStream(pageText);
    objectBodies.push(
      `<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 ${fontObjectNumber} 0 R >> >> /MediaBox [0 0 612 792] /Contents ${contentObjectNumber} 0 R >>`,
      `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    );
  });

  objectBodies.push("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

  let pdf = "%PDF-1.4\n";
  const offsets: number[] = [];
  objectBodies.forEach((body, index) => {
    offsets.push(pdf.length);
    pdf += `${index + 1} 0 obj\n${body}\nendobj\n`;
  });

  const startXref = pdf.length;
  pdf += `xref\n0 ${objectBodies.length + 1}\n0000000000 65535 f \n`;
  for (const offset of offsets) {
    pdf += `${offset.toString().padStart(10, "0")} 00000 n \n`;
  }
  pdf += `trailer\n<< /Size ${objectBodies.length + 1} /Root 1 0 R >>\nstartxref\n${startXref}\n%%EOF\n`;

  assertAscii(pdf);
  return pdf;
}

export function createSampleDocumentFile(key: SampleDocumentKey = "clean"): File {
  const sample = getSampleDocument(key);
  return new File([buildSamplePdf(sample.pages)], sample.fileName, { type: "application/pdf" });
}
