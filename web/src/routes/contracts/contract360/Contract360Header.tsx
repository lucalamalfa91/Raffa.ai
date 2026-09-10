import { Link, useNavigate } from "react-router-dom";
import type { Contract360HeaderBody } from "../../../api/client";
import { getContractTypeLabel } from "../portfolioTableFormatters";
import { formatHeaderMeta, resolveSupplierLabel, type BackLink } from "./contract360ViewModel";

export interface Contract360HeaderProps {
  header: Contract360HeaderBody;
  currency: string;
  docCount: number;
  /** Resolved by the route from `location.state.from`; `null` falls back to a plain "← Back" that walks history. */
  backLink: BackLink | null;
}

/**
 * The V2 Contract 360 header (`contigo-v2/markup.html` "CONTRACT 360" block, top): the origin back
 * link, the supplier kicker over the contract title, and the one-line meta "{type} · {spend} / year
 * · {docCount} documents · {status}" on the right. The two real actions -- "Ask about it" (a new
 * chat scoped to this contract, `/ask?scope=<id>`) and "Review extraction" -- stay with the header.
 *
 * `Contract` has no title field yet, so the h2 reuses the same type-label proxy the Portfolio table's
 * "Contract" column already uses (`getContractTypeLabel`) -- redundant with the meta line's type, but
 * never fabricated.
 */
export default function Contract360Header({ header, currency, docCount, backLink }: Contract360HeaderProps) {
  const navigate = useNavigate();
  const supplier = resolveSupplierLabel(header);

  return (
    <header className="contract360-header">
      {backLink !== null ? (
        <Link to={backLink.href} className="btn btn-ghost contract360-back-link">
          ← {backLink.label}
        </Link>
      ) : (
        <button type="button" className="btn btn-ghost contract360-back-link" onClick={() => navigate(-1)}>
          ← Back
        </button>
      )}
      <div className="contract360-header-row">
        <div>
          <p className="screen-kicker" title={supplier.title}>
            {supplier.label}
          </p>
          <h2 className="screen-title">{getContractTypeLabel(header.type)}</h2>
        </div>
        <div className="contract360-header-side">
          <p className="contract360-header-meta">{formatHeaderMeta(header, currency, docCount)}</p>
          <div className="contract360-header-actions">
            <Link to={`/ask?scope=${header.contractId}`} className="btn btn-secondary">
              Ask about it
            </Link>
            <Link to={`/contracts/${header.contractId}/review`} className="btn btn-ghost">
              Review extraction
            </Link>
          </div>
        </div>
      </div>
    </header>
  );
}
