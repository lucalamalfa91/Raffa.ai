import { describe, expect, it, vi } from "vitest";
import type { ApiClient, UploadDocumentResult } from "../../../src/api/client";
import {
  ACCEPTED_EXTENSIONS,
  MAX_CONCURRENT_UPLOADS,
  MAX_FILES_PER_BATCH,
  MAX_FILE_BYTES,
  getOversizedCopy,
  getRejectionReasonCopy,
  isOversized,
  runUploadBatch,
  type UploadBatchOutcome,
} from "../../../src/routes/documents/uploadPipeline";

function pdfFile(name: string, size = 1024): File {
  return new File([new Uint8Array(size)], name, { type: "application/pdf" });
}

function mockApiClient(uploadDocument: ApiClient["uploadDocument"]): ApiClient {
  return { uploadDocument } as unknown as ApiClient;
}

function ok(document: NonNullable<UploadDocumentResult["document"]>): UploadDocumentResult {
  return { ok: true, statusCode: 201, document, rejection: null, error: null };
}

describe("limits (R-DOC-01, task's own coding objective)", () => {
  it("names the requirements' own multi-file limits", () => {
    expect(MAX_FILES_PER_BATCH).toBe(20);
    expect(MAX_FILE_BYTES).toBe(50 * 1024 * 1024);
    expect(MAX_CONCURRENT_UPLOADS).toBe(3);
    expect(ACCEPTED_EXTENSIONS).toBe(".pdf,.docx,.xlsx,.png,.jpg,.jpeg");
  });
});

describe("isOversized", () => {
  it("flags a file over 50 MB, not a file at or under it", () => {
    expect(isOversized(pdfFile("big.pdf", MAX_FILE_BYTES + 1))).toBe(true);
    expect(isOversized(pdfFile("ok.pdf", MAX_FILE_BYTES))).toBe(false);
  });
});

// R-DOC-04's own verbatim rejection copy.
describe("getRejectionReasonCopy", () => {
  it("maps not_a_contract to the recipe example sentence", () => {
    expect(getRejectionReasonCopy("not_a_contract")).toBe(
      "Not added: this looks like a recipe, not a contract. Raffa.ai only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.",
    );
  });

  it("maps no_readable_text to its own distinct sentence", () => {
    expect(getRejectionReasonCopy("no_readable_text")).toBe(
      "Not added: Raffa.ai could not read any contract text in this file. Try a clearer scan or the original PDF.",
    );
  });
});

describe("getOversizedCopy", () => {
  it("names the file and the 50 MB ceiling", () => {
    expect(getOversizedCopy("huge.pdf")).toBe("Not added: huge.pdf is larger than 50 MB. Raffa.ai accepts files up to 50 MB.");
  });
});

describe("runUploadBatch", () => {
  it("reports an admitted outcome per file, independently (R-DOC-01 AC-1: three rows, no blocking)", async () => {
    const uploadDocument = vi.fn(async (_tenantId: string, file: File) =>
      ok({
        id: `id-${file.name}`,
        contractId: "contract-1",
        fileName: file.name,
        mimeType: "application/pdf",
        processingStatus: "Completed",
        createdAt: "2026-09-06T08:00:00Z",
      }),
    );
    const entries = [
      { key: "a", file: pdfFile("A.pdf") },
      { key: "b", file: pdfFile("B.pdf") },
      { key: "c", file: pdfFile("C.pdf") },
    ];
    const outcomes: UploadBatchOutcome[] = [];

    await runUploadBatch(entries, mockApiClient(uploadDocument), "tenant-1", (outcome) => outcomes.push(outcome));

    expect(uploadDocument).toHaveBeenCalledTimes(3);
    expect(outcomes).toHaveLength(3);
    expect(outcomes.every((outcome) => outcome.kind === "admitted")).toBe(true);
  });

  it("never runs more than MAX_CONCURRENT_UPLOADS requests at once", async () => {
    let inFlight = 0;
    let maxInFlight = 0;
    const resolvers: Array<() => void> = [];
    const uploadDocument = vi.fn(
      (_tenantId: string, file: File) =>
        new Promise<UploadDocumentResult>((resolve) => {
          inFlight += 1;
          maxInFlight = Math.max(maxInFlight, inFlight);
          resolvers.push(() => {
            inFlight -= 1;
            resolve(
              ok({
                id: file.name,
                contractId: "contract-1",
                fileName: file.name,
                mimeType: "application/pdf",
                processingStatus: "Completed",
                createdAt: "2026-09-06T08:00:00Z",
              }),
            );
          });
        }),
    );
    const entries = Array.from({ length: 6 }, (_, i) => ({ key: String(i), file: pdfFile(`F${i}.pdf`) }));

    const batchPromise = runUploadBatch(entries, mockApiClient(uploadDocument), "tenant-1", () => {});

    // Let the microtask queue settle so every worker has had a chance to start its first request.
    await Promise.resolve();
    await Promise.resolve();
    expect(maxInFlight).toBeLessThanOrEqual(MAX_CONCURRENT_UPLOADS);
    expect(inFlight).toBeLessThanOrEqual(MAX_CONCURRENT_UPLOADS);

    while (resolvers.length > 0) {
      resolvers.shift()!();
      // eslint-disable-next-line no-await-in-loop
      await Promise.resolve();
    }
    await batchPromise;
    expect(maxInFlight).toBeLessThanOrEqual(MAX_CONCURRENT_UPLOADS);
  });

  it("reports a rejected outcome (with the mapped copy) on a 422, never calling it 'failed'", async () => {
    const uploadDocument = vi.fn(async () => ({
      ok: false,
      statusCode: 422,
      document: null,
      rejection: { rejected: true as const, detectedType: "Other" as const, confidence: 0.93, reason: "not_a_contract" as const, hint: "..." },
      error: null,
    }));
    const outcomes: UploadBatchOutcome[] = [];

    await runUploadBatch([{ key: "a", file: pdfFile("recipe.pdf") }], mockApiClient(uploadDocument), "tenant-1", (outcome) =>
      outcomes.push(outcome),
    );

    expect(outcomes).toEqual([
      {
        kind: "rejected",
        key: "a",
        fileName: "recipe.pdf",
        message:
          "Not added: this looks like a recipe, not a contract. Raffa.ai only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.",
      },
    ]);
  });

  it("reports a rejected outcome on a 415, using the server's own format message", async () => {
    const uploadDocument = vi.fn(async () => ({
      ok: false,
      statusCode: 415,
      document: null,
      rejection: null,
      error: "Raffa reads PDF, Word, Excel and scanned images",
    }));
    const outcomes: UploadBatchOutcome[] = [];

    await runUploadBatch([{ key: "a", file: pdfFile("archive.zip") }], mockApiClient(uploadDocument), "tenant-1", (outcome) =>
      outcomes.push(outcome),
    );

    expect(outcomes).toEqual([
      { kind: "rejected", key: "a", fileName: "archive.zip", message: "Raffa reads PDF, Word, Excel and scanned images" },
    ]);
  });

  it("reports an oversized file as rejected without ever calling the API (R-DOC-01)", async () => {
    const uploadDocument = vi.fn();
    const outcomes: UploadBatchOutcome[] = [];

    await runUploadBatch(
      [{ key: "a", file: pdfFile("huge.pdf", MAX_FILE_BYTES + 1) }],
      mockApiClient(uploadDocument),
      "tenant-1",
      (outcome) => outcomes.push(outcome),
    );

    expect(uploadDocument).not.toHaveBeenCalled();
    expect(outcomes).toEqual([
      { kind: "rejected", key: "a", fileName: "huge.pdf", message: "Not added: huge.pdf is larger than 50 MB. Raffa.ai accepts files up to 50 MB." },
    ]);
  });

  it("reports a generic transport/400 failure as 'failed', distinct from a rejection", async () => {
    const uploadDocument = vi.fn(async () => ({
      ok: false,
      statusCode: null,
      document: null,
      rejection: null,
      error: "Unable to reach the API. Cause: network down",
    }));
    const outcomes: UploadBatchOutcome[] = [];

    await runUploadBatch([{ key: "a", file: pdfFile("A.pdf") }], mockApiClient(uploadDocument), "tenant-1", (outcome) =>
      outcomes.push(outcome),
    );

    expect(outcomes).toEqual([{ kind: "failed", key: "a", fileName: "A.pdf", message: "Unable to reach the API. Cause: network down" }]);
  });
});
