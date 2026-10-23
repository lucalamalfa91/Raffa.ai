import type { ReactNode } from "react";

/** A section title: 11px uppercase on a 2px ink rule. */
export function SectionHead({ children }: { children: ReactNode }) {
  return <h4 className="ct-head">{children}</h4>;
}

/** A line of muted text standing in for an empty section. */
export function Empty({ children }: { children: ReactNode }) {
  return <p className="ct-empty">{children}</p>;
}
