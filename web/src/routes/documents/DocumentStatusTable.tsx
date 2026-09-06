import { Link } from "react-router-dom";
import { formatUploadedAt, getDocumentStatusTag, getDocumentTypeLabel } from "./documentTable";
import type { TrackedDocument } from "./documentStore";

export interface DocumentStatusTableProps {
  documents: readonly TrackedDocument[];
}

/**
 * ADR-020 screen 3's *other* half ("screen 3 may be two: upload UI +
 * document-status read-back"). AC-1 (table), AC-2 (status tags, ADR-019),
 * AC-3 (row cross-link). Column order/labels are quoted verbatim from
 * screens.md #3: "Document table: Document / Type / Supplier / Status /
 * Uploaded, rows open Contract 360". Uses the shared `.table` class
 * (`src/styles/components.css`, ADR-019 component catalogue) -- no new table
 * styling invented here, only the column-width composite in documents.css.
 */
export default function DocumentStatusTable({ documents }: DocumentStatusTableProps) {
  if (documents.length === 0) {
    return (
      <div className="empty-state" role="status">
        <h4>No documents yet</h4>
        <p className="micro-meta">Uploaded documents appear here once processing finishes.</p>
      </div>
    );
  }

  return (
    <table className="table document-status-table">
      <thead>
        <tr>
          <th scope="col">Document</th>
          <th scope="col">Type</th>
          <th scope="col">Supplier</th>
          <th scope="col">Status</th>
          <th scope="col">Uploaded</th>
        </tr>
      </thead>
      <tbody>
        {documents.map((document) => {
          const tag = getDocumentStatusTag(document.processingStatus);
          return (
            <tr key={document.id}>
              <td>
                {document.contractId !== null ? (
                  // AC-3 "rows open Contract 360" (ia.md "Cross-links": "Document
                  // row -> Contract 360"). A real <Link> (react-router-dom), the
                  // same navigation-primitive RailNav.tsx already uses -- not a
                  // <button onClick={() => navigate(...)}>, since this is a plain
                  // navigation, not an in-page action (ADR-019 accessibility
                  // baseline: every interactive control is native).
                  <Link to={`/contracts/${document.contractId}`} className="document-status-table-link">
                    {document.fileName}
                  </Link>
                ) : (
                  <>
                    <span>{document.fileName}</span>
                    {/* contractId stays null when processing failed before
                        classification could link a contract -- there is
                        nothing for this row to cross-link to yet. A visible
                        reason, not a silently-dead link (ADR-019
                        accessibility baseline). */}
                    <span className="hint">Not yet linked to a contract</span>
                  </>
                )}
              </td>
              <td>{getDocumentTypeLabel(document.documentType)}</td>
              <td>
                {/* Document carries no supplier field anywhere in the API
                    surface this client can reach: `Document`
                    (backend/.../Contigo.Documents.Contracts/Domain/Document.cs)
                    has no supplier column at all; `Contract.SupplierId`
                    exists but is an id-only cross-module reference (ADR-002
                    module map), and even the Portfolio list
                    (`GET /api/contracts`, `PortfolioEndpointExtensions.cs`)
                    returns that raw id, never a resolved supplier name.
                    Rendered as an honest placeholder, not a fabricated
                    value -- see this task's own README note. */}
                <span className="micro-meta">Not yet available</span>
              </td>
              <td>
                <span className={`tag tag-${tag.variant}`}>{tag.label}</span>
              </td>
              <td>{formatUploadedAt(document.createdAt)}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
