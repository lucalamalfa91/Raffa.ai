import type { ContractFieldEvidenceBody } from "../../../api/client";

/**
 * Bounding-box overlay for the document viewer (epic-23 feature-04, task E23/F04/US01/T01,
 * NW-63r; ADR-029 w18 footer clauses 2-3; ADR-012 §3, ADR-018 w17 clause 9). Absolutely-positioned
 * DOM over the existing page `<img>` -- no canvas, no new runtime dependency (`web/package.json`
 * keeps its fixed dependency set). A `null` box (every row written before this wave, or a page the
 * OCR call returned no layout geometry for) renders nothing here. The box is
 * additive over the page image, never a replacement for it.
 *
 * Geometry source: `GET /api/contracts/{id}/evidence` (epic-23 feature-02, ADR-003 w18 footer
 * clauses 1-2), *not* the Contract 360 "Why" clauses this route already fetches for
 * `ClauseHighlight` -- `Clause`/`Contract360ClauseBody` is a separate domain object with no box
 * column and no shared key with `ExtractionEvidence`/`ContractFieldEvidenceBody` (verified:
 * `backend/src/Raffa.Documents.Contracts/Domain/Clause.cs` carries its own `SourceSpan`/`SourcePage`,
 * never a `FieldName`). So this route reads the contract's field evidence independently
 * (`index.tsx`'s own fetch) and this module selects, from that list, every field whose evidence
 * sits on the page currently on screen -- not only the one field a `?clause=` citation happened to
 * name. A page can carry more than one cited phrase (screens-v2.md:95-112), so every match renders
 * its own box.
 */

/** The wire shape `ContractFieldEvidenceBody.box` carries when non-null -- pixel-space on the
 * *rendered* page image (the C# `ContractFieldEvidenceBox` doc comment's own words), i.e. against
 * the same pixel grid `naturalWidth`/`naturalHeight` below read off the `<img>`. */
export type EvidenceBox = NonNullable<ContractFieldEvidenceBody["box"]>;

export interface PageBoxSpec {
  /** `fieldName` -- unique per contract, stable across reloads, so it doubles as the React key. */
  key: string;
  box: EvidenceBox;
}

/**
 * Which of the contract's field-evidence boxes belong on `page` of `documentId`. A field's box
 * counts only when its own `sourceDocumentId`/`sourcePage` match what is on screen -- a box
 * computed for another page or another document must never bleed onto this one (the same
 * per-document, per-page discipline `resolveCitation` already applies to clauses). A `null` box
 * is filtered out here, not passed down as an empty rectangle.
 */
export function selectPageBoxes(
  evidence: readonly ContractFieldEvidenceBody[],
  documentId: string,
  page: number,
): PageBoxSpec[] {
  const boxes: PageBoxSpec[] = [];
  for (const entry of evidence) {
    if (entry.sourceDocumentId === documentId && entry.sourcePage === page && entry.box !== null) {
      boxes.push({ key: entry.fieldName, box: entry.box });
    }
  }
  return boxes;
}

export interface BoxRect {
  left: string;
  top: string;
  width: string;
  height: string;
}

/**
 * `box` is pixel-space on the rendered page image, so a percentage of that same image's *natural*
 * size positions it correctly under this canvas's `object-fit: contain` regardless of how large the
 * viewport ends up rendering the page -- no measurement library, the same "no vector layer needed in
 * V1" posture ADR-029 states. `null` while the image has not reported its natural size yet (nothing
 * to divide by), so the caller skips rendering that frame rather than drawing at 0,0.
 *
 * Known V1 simplification, recorded rather than silently assumed: this places the box against the
 * canvas's own box, which is exactly right when the image fills it edge to edge and only
 * approximately right if `object-fit: contain` ever letterboxes (the canvas is a fixed 210/297 (A4)
 * aspect ratio; a source page with a materially different ratio would letterbox). Real-world
 * uploaded contract pages are close enough to that ratio for a V1 highlighter-style box; a pixel-exact
 * fix needs the image's rendered content-box offset, not only its natural size.
 */
export function computeBoxRect(box: EvidenceBox, naturalWidth: number, naturalHeight: number): BoxRect | null {
  if (naturalWidth <= 0 || naturalHeight <= 0) return null;
  const pct = (value: number, total: number) => `${(value / total) * 100}%`;
  return {
    left: pct(box.x, naturalWidth),
    top: pct(box.y, naturalHeight),
    width: pct(box.width, naturalWidth),
    height: pct(box.height, naturalHeight),
  };
}

export interface BoxOverlayProps {
  boxes: readonly PageBoxSpec[];
  /** The page `<img>`'s own `naturalWidth`/`naturalHeight` (its `onLoad`), or `0` before it has
   * loaded. `0` degrades to "draw nothing" via {@link computeBoxRect}, never a divide-by-zero. */
  naturalWidth: number;
  naturalHeight: number;
}

/**
 * Renders one positioned `<div>` per cited phrase (ADR-029 clause 2: "the box is drawn on the page
 * over the cited phrase"), scaled to the rendered image. Renders nothing when there is nothing to
 * draw -- an empty `boxes` list or an image that has not reported its natural size yet.
 */
export default function BoxOverlay({ boxes, naturalWidth, naturalHeight }: BoxOverlayProps) {
  if (boxes.length === 0) return null;

  const rects = boxes
    .map(({ key, box }) => ({ key, rect: computeBoxRect(box, naturalWidth, naturalHeight) }))
    .filter((entry): entry is { key: string; rect: BoxRect } => entry.rect !== null);

  if (rects.length === 0) return null;

  return (
    <div className="document-viewer-box-layer" aria-hidden="true">
      {rects.map(({ key, rect }) => (
        <div key={key} className="document-viewer-box" data-testid="document-viewer-box" style={rect} />
      ))}
    </div>
  );
}
