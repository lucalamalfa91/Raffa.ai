/**
 * The two "Sample MSA" files (`UploadDropzone.tsx`'s sample buttons, both dropzone variants). This
 * repo ships no real sample contract asset, so each sample is a small PDF built in the browser and
 * sent through the exact same `apiClient.uploadDocument()` call (via `useDocumentsList.ts#uploadFiles`)
 * a real drag-and-drop/file-picker upload uses -- a real end-to-end request against the deployed
 * API, not a client-only mock.
 *
 * **Two samples, two suppliers, two honest outcomes.** The outcome is still whatever the deployed
 * pipeline really returns; the texts are written so that outcome is a *meaningful* one:
 *
 * - `clean` -- an MSA with Northwind Traders SA, every commercial term stated once, plainly, with
 *   the supplier role labelled. The fixture extractor (and any reasonable model) reads every field
 *   with high confidence, so the document completes and is askable straight away.
 * - `needs-review` -- an MSA with Fabrikam Software GmbH whose text is genuinely ambiguous: the
 *   parties are named without saying which one supplies, the body and Schedule 1 disagree on the
 *   annual fee, and the renewal clause both affirms and denies automatic renewal. Exactly those
 *   fields come back weak, the document lands in `needs_review`, and the review screen has real
 *   evidence (page, quoted span, confidence) to show for each of them.
 *
 * **It has to be a readable contract, not a placeholder.** Each page is a syntactically real PDF
 * page object paired 1:1 with an uncompressed `BT ... Tj ... ET` content stream -- exactly the shape
 * `NativeDocumentTextExtractor` reads natively (it counts `/Type /Page` objects and pairs them with
 * text streams in file order, joining a stream's literals with spaces), so the samples take the
 * born-digital path, never spend an OCR call, clear `Documents:MinReadableChars` (200) comfortably,
 * and carry the words `MASTER SERVICES AGREEMENT` the classifier keys on. Text stays within Latin-1
 * (the encoding the extractor decodes an uncompressed stream with) and every `(`, `)` and `\` is
 * escaped as the PDF string syntax requires.
 */

export type SampleDocumentKey = "clean" | "needs-review";

export interface SampleDocumentDefinition {
  key: SampleDocumentKey;
  /** Button label, identical in both dropzone variants. */
  label: string;
  /** One sentence for the button's `title`: who the supplier is and what to expect. */
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
    description: "Northwind Traders SA — every term stated plainly; expected to complete without review.",
    fileName: "contigo-sample-northwind-msa.pdf",
    supplier: "Northwind Traders SA",
    pages: [
      [
        "MASTER SERVICES AGREEMENT",
        'This Master Services Agreement is entered into between Contigo Demo AG ("Customer") and Northwind Traders SA ("Supplier"), effective 2026-01-01.',
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
    description: "Fabrikam Software GmbH — ambiguous supplier, fee and renewal clauses; expected to need review.",
    fileName: "contigo-sample-fabrikam-msa.pdf",
    supplier: "Fabrikam Software GmbH",
    pages: [
      [
        "MASTER SERVICES AGREEMENT",
        "This Master Services Agreement is made between Contigo Demo AG and Fabrikam Software GmbH, effective 1 February 2026.",
        "1. Scope. Fabrikam provides its analytics platform and support services to Contigo Demo AG under the Order Forms executed from time to time.",
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

/** Word-wraps one paragraph; the native extractor joins a stream's literals with single spaces, so
 * wrapping on spaces reproduces the paragraph text exactly. */
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

/**
 * A minimal but syntactically real multi-page PDF: one catalog, one page tree, then per page a
 * `/Type /Page` object and its own uncompressed content stream, and one shared Helvetica font --
 * the object count `NativeDocumentTextExtractor` needs to pair pages with streams 1:1. No xref
 * table: the extractor's lightweight scan never reads it (see that type's own doc comment), and
 * a browser preview is not what these bytes are for.
 */
export function buildSamplePdf(pages: readonly string[]): string {
  const pageCount = pages.length;
  const fontObjectNumber = 3 + pageCount * 2;
  const kids = pages.map((_, index) => `${3 + index * 2} 0 R`).join(" ");

  const objects: string[] = [
    "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
    `2 0 obj << /Type /Pages /Kids [${kids}] /Count ${pageCount} >> endobj`,
  ];

  pages.forEach((pageText, index) => {
    const pageObjectNumber = 3 + index * 2;
    const contentObjectNumber = pageObjectNumber + 1;
    const stream = buildContentStream(pageText);
    objects.push(
      `${pageObjectNumber} 0 obj << /Type /Page /Parent 2 0 R /Resources << /Font << /F1 ${fontObjectNumber} 0 R >> >> /MediaBox [0 0 612 792] /Contents ${contentObjectNumber} 0 R >> endobj`,
      `${contentObjectNumber} 0 obj << /Length ${stream.length} >>`,
      "stream",
      stream,
      "endstream",
      "endobj",
    );
  });

  objects.push(`${fontObjectNumber} 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj`);

  return ["%PDF-1.4", ...objects, "trailer << /Root 1 0 R >>", "%%EOF"].join("\n");
}

export function createSampleDocumentFile(key: SampleDocumentKey = "clean"): File {
  const sample = getSampleDocument(key);
  return new File([buildSamplePdf(sample.pages)], sample.fileName, { type: "application/pdf" });
}
