import { describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach } from "vitest";
import type { ContractFieldEvidenceBody } from "../../../../src/api/client";
import BoxOverlay, { computeBoxRect, selectPageBoxes, type PageBoxSpec } from "../../../../src/routes/documents/viewer/BoxOverlay";

const DOCUMENT_ID = "55555555-5555-5555-5555-555555555555";

function evidenceRow(overrides: Partial<ContractFieldEvidenceBody> = {}): ContractFieldEvidenceBody {
  return {
    fieldName: "startDate",
    value: "2026-01-01",
    confidence: 0.92,
    decision: "auto_accepted",
    sourcePage: 3,
    sourceSpan: "1 January 2026",
    box: { x: 120, y: 340, width: 200, height: 40 },
    sourceDocumentId: DOCUMENT_ID,
    sourceFileName: "Acme_MSA.pdf",
    passage: "...effective 1 January 2026...",
    highlightStart: 10,
    highlightLength: 15,
    modelId: "gpt-x",
    extractedAt: "2026-09-01T00:00:00Z",
    ...overrides,
  };
}

describe("selectPageBoxes (task E23/F04/US01/T01)", () => {
  it("keeps only entries for this document and this page whose box is non-null", () => {
    const rows: ContractFieldEvidenceBody[] = [
      evidenceRow({ fieldName: "startDate", sourcePage: 3 }),
      evidenceRow({ fieldName: "endDate", sourcePage: 4 }), // wrong page
      evidenceRow({ fieldName: "supplier", sourceDocumentId: "other-doc", sourcePage: 3 }), // wrong document
      evidenceRow({ fieldName: "currency", sourcePage: 3, box: null }), // no geometry: text highlight only
    ];

    const boxes = selectPageBoxes(rows, DOCUMENT_ID, 3);

    expect(boxes).toEqual<PageBoxSpec[]>([{ key: "startDate", box: { x: 120, y: 340, width: 200, height: 40 } }]);
  });

  it("renders one entry per qualifying field -- a page can carry more than one cited phrase", () => {
    const rows: ContractFieldEvidenceBody[] = [
      evidenceRow({ fieldName: "startDate", sourcePage: 3 }),
      evidenceRow({ fieldName: "endDate", sourcePage: 3, box: { x: 10, y: 20, width: 30, height: 40 } }),
    ];

    expect(selectPageBoxes(rows, DOCUMENT_ID, 3).map((b) => b.key)).toEqual(["startDate", "endDate"]);
  });

  it("an empty evidence list yields no boxes", () => {
    expect(selectPageBoxes([], DOCUMENT_ID, 1)).toEqual([]);
  });
});

describe("computeBoxRect (task E23/F04/US01/T01)", () => {
  it("scales a pixel-space box to a percentage of the image's natural size", () => {
    expect(computeBoxRect({ x: 100, y: 50, width: 200, height: 40 }, 1000, 2000)).toEqual({
      left: "10%",
      top: "2.5%",
      width: "20%",
      height: "2%",
    });
  });

  it("returns null before the image has reported a natural size (never divides by zero)", () => {
    expect(computeBoxRect({ x: 0, y: 0, width: 10, height: 10 }, 0, 0)).toBeNull();
    expect(computeBoxRect({ x: 0, y: 0, width: 10, height: 10 }, 100, 0)).toBeNull();
  });
});

describe("BoxOverlay (task E23/F04/US01/T01, AC-1)", () => {
  afterEach(() => {
    cleanup();
  });

  it("a non-null box renders a positioned overlay div scaled to the rendered image", () => {
    const boxes: PageBoxSpec[] = [{ key: "startDate", box: { x: 100, y: 200, width: 300, height: 60 } }];
    render(<BoxOverlay boxes={boxes} naturalWidth={1000} naturalHeight={2000} />);

    const box = screen.getByTestId("document-viewer-box");
    expect(box).toBeInTheDocument();
    expect(box).toHaveStyle({ left: "10%", top: "10%", width: "30%", height: "3%" });
  });

  it("renders one div per box when a page carries more than one cited phrase", () => {
    const boxes: PageBoxSpec[] = [
      { key: "startDate", box: { x: 0, y: 0, width: 10, height: 10 } },
      { key: "endDate", box: { x: 50, y: 50, width: 10, height: 10 } },
    ];
    render(<BoxOverlay boxes={boxes} naturalWidth={100} naturalHeight={100} />);

    expect(screen.getAllByTestId("document-viewer-box")).toHaveLength(2);
  });

  it("an empty box list renders nothing -- the null-box / no-citation case degrades to the text highlight alone", () => {
    const { container } = render(<BoxOverlay boxes={[]} naturalWidth={1000} naturalHeight={2000} />);
    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByTestId("document-viewer-box")).not.toBeInTheDocument();
  });

  it("a box with a natural size of zero (image not yet loaded) renders nothing rather than a mispositioned box", () => {
    const boxes: PageBoxSpec[] = [{ key: "startDate", box: { x: 100, y: 200, width: 300, height: 60 } }];
    const { container } = render(<BoxOverlay boxes={boxes} naturalWidth={0} naturalHeight={0} />);
    expect(container).toBeEmptyDOMElement();
  });
});
