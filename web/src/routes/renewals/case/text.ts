/** Number and money formatting for the renewal case (English digits: the contracts are CHF). */

export function num(n: number): string {
  return Math.round(n).toLocaleString("en-US");
}

export function chf(n: number): string {
  return `CHF ${num(n)}`;
}
