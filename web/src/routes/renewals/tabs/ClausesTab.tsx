import type { CSSProperties } from "react";
import { SectionHead } from "./TabParts";
import type { TabProps } from "./NowTab";

/** What to expect in the renegotiation, clause by clause: general practice, never a market benchmark. */
export default function ClausesTab({ v, w }: TabProps) {
  const { t } = w;
  const grid: CSSProperties = { gridTemplateColumns: v.outlook.layout.map((c) => `minmax(${c.min}px, ${c.fr}fr)`).join(" ") };
  return (
    <section>
      <SectionHead>{t("What to expect in the renegotiation")}</SectionHead>
      <p className="ct-note">
        {t("Read from the contract, clause by clause, against common practice for this kind of agreement. General practice, not a market benchmark — the supplier may differ.")}
      </p>
      <div className="ct-table-scroll">
        <table className="ct-table" role="table" aria-label={t("What to expect in the renegotiation")}>
          <thead role="rowgroup">
            <tr role="row" style={grid}>
              {v.outlook.cols.map((col) => (
                <th role="columnheader" scope="col" key={col}>
                  {col}
                </th>
              ))}
            </tr>
          </thead>
          <tbody role="rowgroup">
            {v.outlook.rows.map((cells, r) => (
              <tr role="row" key={r} style={grid}>
                {cells.map((cell, i) => (
                  <td role="cell" key={i} style={cell.w ? { fontWeight: cell.w } : undefined}>
                    {cell.v}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
