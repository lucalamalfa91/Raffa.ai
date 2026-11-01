/**
 * Links into Risultati from other screens. The 4.0 Risultati page shows verifiable results for the
 * whole workspace; the contract id travels along for context and is ignored by the page.
 */
export function buildSavingsContractHref(contractId: string): string {
  return `/savings?contract=${encodeURIComponent(contractId)}`;
}
