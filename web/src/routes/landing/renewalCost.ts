/**
 * The landing page's "It's only 2%" calculator: what a yearly price increase costs over the next
 * renewal years compared with keeping today's price. Year n costs `current × (1 + uplift)ⁿ`; the
 * first increase applies at the next renewal.
 */
export interface RenewalCostRow {
  year: number;
  annual: number;
  extra: number;
}

export interface RenewalCost {
  rows: RenewalCostRow[];
  baseline: number;
  total: number;
  extra: number;
  /** How much higher the annual price is after the last year, in percent. */
  finalIncrease: number;
}

export function computeRenewalCost(current: number, upliftPercent: number, years: number): RenewalCost {
  if (
    !Number.isFinite(current) ||
    current < 0 ||
    current > 1e9 ||
    !Number.isFinite(upliftPercent) ||
    upliftPercent < 0 ||
    upliftPercent > 25 ||
    !Number.isInteger(years) ||
    years < 1 ||
    years > 10
  ) {
    throw new Error("Invalid calculator values");
  }
  const rows = Array.from({ length: years }, (_, index) => {
    const annual = current * (1 + upliftPercent / 100) ** (index + 1);
    return { year: index + 1, annual, extra: annual - current };
  });
  return {
    rows,
    baseline: current * years,
    total: rows.reduce((sum, row) => sum + row.annual, 0),
    extra: rows.reduce((sum, row) => sum + row.extra, 0),
    finalIncrease: ((1 + upliftPercent / 100) ** years - 1) * 100,
  };
}

/** `12345.6` → `"12,346"`; with `decimals` the fraction is kept (`"12,345.60"`). */
export function formatAmount(value: number, decimals = 0): string {
  const [whole, fraction] = value.toFixed(decimals).split(".");
  return whole.replace(/\B(?=(\d{3})+(?!\d))/g, ",") + (fraction ? `.${fraction}` : "");
}

/** Compact form for large amounts: `1.25m`, `3.10bn`, `102k`. */
export function formatCompact(value: number): string {
  if (value >= 1e9) return `${formatAmount(value / 1e9, 2)}bn`;
  if (value >= 1e6) return `${formatAmount(value / 1e6, 2)}m`;
  if (value >= 1e3) return `${formatAmount(value / 1e3, 0)}k`;
  return formatAmount(value);
}
