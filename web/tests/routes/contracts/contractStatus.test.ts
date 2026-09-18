import { describe, expect, it } from "vitest";
import {
  isContractReadyToUse,
  isDocumentNeedsReview,
  isDocumentPending,
  isValidatedContractStatus,
} from "../../../src/routes/contracts/contractStatus";

describe("isContractReadyToUse (shared Ready rule for Portfolio and Renewals)", () => {
  it("is ready only when contract status is validated and the document is not pending or in review", () => {
    expect(isContractReadyToUse("active", "Completed")).toBe(true);
    expect(isContractReadyToUse("Completed", "Completed")).toBe(true);
    expect(isContractReadyToUse("needs_review", "NeedsReview")).toBe(false);
    expect(isContractReadyToUse("active", "NeedsReview")).toBe(false);
    expect(isContractReadyToUse("processing", "Uploaded")).toBe(false);
    expect(isContractReadyToUse("processing", "Processing")).toBe(false);
    expect(isValidatedContractStatus("needs_review")).toBe(false);
    expect(isDocumentPending("Uploaded")).toBe(true);
    expect(isDocumentNeedsReview("NeedsReview")).toBe(true);
  });
});
