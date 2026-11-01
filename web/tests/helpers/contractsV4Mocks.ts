import { vi } from "vitest";
import type { ApiClient } from "../../src/api/client";

type ContractsV4Methods = Pick<
  ApiClient,
  | "uploadSampleDocument"
  | "getDocumentReading"
  | "decideReadingField"
  | "listContractsV4"
  | "getContractV4"
  | "setDecisionPoint"
  | "openRenewalCase"
>;

/** The AskRaffa 4.0 slice-4 client methods as plain mocks, for hand-built `ApiClient` doubles. */
export function contractsV4Mocks(): ContractsV4Methods {
  // Default answer: not found (each method never throws, like the real client).
  const notFound = () => vi.fn().mockResolvedValue({ ok: false, statusCode: 404, data: null, error: null });
  return {
    uploadSampleDocument: notFound(),
    getDocumentReading: notFound(),
    decideReadingField: notFound(),
    listContractsV4: notFound(),
    getContractV4: notFound(),
    setDecisionPoint: notFound(),
    openRenewalCase: notFound(),
  };
}
