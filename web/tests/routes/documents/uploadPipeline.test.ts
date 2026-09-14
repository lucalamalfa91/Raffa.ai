import { afterEach, describe, expect, it, vi } from "vitest";
import type { ApiClient, UploadDocumentResult } from "../../../src/api/client";
import {
  ACCEPTED_EXTENSIONS,
  MAX_CONCURRENT_UPLOADS,
  MAX_FILES_PER_BATCH,
  MAX_FILE_BYTES,
  UPLOAD_DEADLINE_MS,
  getOversizedCopy,
  getRefusalCopy,
  getRejectionReasonCopy,
  getTransportFailureCopy,
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

/** The 201 since task E16/F02/US03/T01 (ADR-027 §D1): stored and queued, no contract yet. */
function stored(document: Partial<NonNullable<UploadDocumentResult["document"]>> & { id: string; fileName: string }): UploadDocumentResult {
  return {
    ok: true,
    statusCode: 201,
    document: {
      contractId: null,
      mimeType: "application/pdf",
      processingStatus: "Uploaded",
      createdAt: "2026-09-06T08:00:00Z",
      ...document,
    },
    rejection: null,
    error: null,
  };
}

afterEach(() => {
  vi.useRealTimers();
});

describe("limits (R-DOC-01, task's own coding objective; ADR-012 w15 §5 deadline)", () => {
  it("names the requirements' own multi-file limits and the one client-owned upload deadline", () => {
    expect(MAX_FILES_PER_BATCH).toBe(20);
    expect(MAX_FILE_BYTES).toBe(50 * 1024 * 1024);
    expect(MAX_CONCURRENT_UPLOADS).toBe(3);
    expect(ACCEPTED_EXTENSIONS).toBe(".pdf,.docx,.xlsx,.png,.jpg,.jpeg");
    expect(UPLOAD_DEADLINE_MS).toBe(120_000);
  });
});

describe("isOversized", () => {
  it("flags a file over 50 MB, not a file at or under it", () => {
    expect(isOversized(pdfFile("big.pdf", MAX_FILE_BYTES + 1))).toBe(true);
    expect(isOversized(pdfFile("ok.pdf", MAX_FILE_BYTES))).toBe(false);
  });
});

// R-DOC-04's own rejection copy, keyed by the Worker's reason code on a `Rejected` row (ADR-027
// §D6) -- minus the "Not added: " lead-in the row's own tag now carries (ADR-020 w15 §1.3).
describe("getRejectionReasonCopy", () => {
  it("maps not_a_contract to the recipe example sentence, without the retired lead-in", () => {
    expect(getRejectionReasonCopy("not_a_contract")).toBe(
      "This looks like a recipe, not a contract. Raffa.ai only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.",
    );
  });

  it("maps no_readable_text to its own distinct sentence", () => {
    expect(getRejectionReasonCopy("no_readable_text")).toBe(
      "Raffa.ai could not read any contract text in this file. Try a clearer scan or the original PDF.",
    );
  });

  it("is silent -- null, never a guessed sentence -- for no code or an unknown one (ADR-020 w15 §7)", () => {
    expect(getRejectionReasonCopy(null)).toBeNull();
    expect(getRejectionReasonCopy("some_future_code")).toBeNull();
  });
});

describe("the refusals that never reach the server (ADR-020 w15 §6)", () => {
  it("names the 50 MB ceiling for a file this browser refused, without repeating the filename the row already shows", () => {
    expect(getOversizedCopy()).toBe("This file is larger than 50 MB, the most Raffa.ai accepts.");
  });

  it("has one designed sentence per in-request refusal, and the 413 one never restates a limit the client cannot vouch for", () => {
    expect(getRefusalCopy(413)).toBe("This file is too large for Raffa.ai to accept. Try a smaller file, or split it into parts.");
    expect(getRefusalCopy(413)).not.toMatch(/50 MB/);
    expect(getRefusalCopy(415)).toBe("Raffa.ai cannot open this file type. Try the original PDF, or a clear scan of the signed pages.");
  });

  it("names the file in the retryable transport sentence, in the product's own name", () => {
    expect(getTransportFailureCopy("A.pdf")).toBe("Raffa.ai could not process A.pdf. Try again.");
  });
});

describe("runUploadBatch", () => {
  it("reports an admitted outcome per file, independently, passing each request its own abort signal (R-DOC-01 AC-1)", async () => {
    const uploadDocument = vi.fn(async (_tenantId: string, file: File, _options?: { signal?: AbortSignal }) =>
      stored({ id: `id-${file.name}`, fileName: file.name }),
    );
    const entries = [
      { key: "a", file: pdfFile("A.pdf") },
      { key: "b", file: pdfFile("B.pdf") },
      { key: "c", file: pdfFile("C.pdf") },
    ];
    const outcomes: UploadBatchOutcome[] = [];

    await runUploadBatch(entries, mockApiClient(uploadDocument), "tenant-1", (outcome) => outcomes.push(outcome));

    expect(uploadDocument).toHaveBeenCalledTimes(3);
    for (const call of uploadDocument.mock.calls) {
      expect(call[2]).toEqual({ signal: expect.any(AbortSignal) });
    }
    expect(outcomes).toHaveLength(3);
    expect(outcomes.every((outcome) => outcome.kind === "admitted")).toBe(true);
    expect(outcomes.map((outcome) => (outcome.kind === "admitted" ? outcome.document.id : null))).toEqual(["id-A.pdf", "id-B.pdf", "id-C.pdf"]);
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
            resolve(stored({ id: file.name, fileName: file.name }));
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

  it("reports a 415 as rejected with the designed sentence -- the server's prose is never rendered (ADR-020 w15 §6.3)", async () => {
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
      {
        kind: "rejected",
        key: "a",
        fileName: "archive.zip",
        message: "Raffa.ai cannot open this file type. Try the original PDF, or a clear scan of the signed pages.",
      },
    ]);
  });

  it("reports a 413 as rejected with its own designed sentence", async () => {
    const uploadDocument = vi.fn(async () => ({ ok: false, statusCode: 413, document: null, rejection: null, error: "too big" }));
    const outcomes: UploadBatchOutcome[] = [];

    await runUploadBatch([{ key: "a", file: pdfFile("scan.pdf") }], mockApiClient(uploadDocument), "tenant-1", (outcome) =>
      outcomes.push(outcome),
    );

    expect(outcomes).toEqual([
      { kind: "rejected", key: "a", fileName: "scan.pdf", message: "This file is too large for Raffa.ai to accept. Try a smaller file, or split it into parts." },
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
      { kind: "rejected", key: "a", fileName: "huge.pdf", message: "This file is larger than 50 MB, the most Raffa.ai accepts." },
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

  // ADR-012 w15 §5: one client-owned deadline; a request that hangs is aborted and lands in the
  // existing "failed" branch with Raffa.ai's own retryable sentence, never an opaque platform 502.
  it("aborts a request that outlives the deadline and reports it as a retryable 'failed' outcome", async () => {
    vi.useFakeTimers();
    const uploadDocument = vi.fn(
      (_tenantId: string, _file: File, options?: { signal?: AbortSignal }) =>
        new Promise<UploadDocumentResult>((resolve) => {
          options?.signal?.addEventListener("abort", () =>
            resolve({ ok: false, statusCode: null, document: null, rejection: null, error: "Unable to reach the API. Cause: The operation was aborted." }),
          );
        }),
    );
    const outcomes: UploadBatchOutcome[] = [];

    const batch = runUploadBatch([{ key: "a", file: pdfFile("slow.pdf") }], mockApiClient(uploadDocument), "tenant-1", (outcome) => outcomes.push(outcome), 1_000);
    await vi.advanceTimersByTimeAsync(999);
    expect(outcomes).toEqual([]);
    await vi.advanceTimersByTimeAsync(1);
    await batch;

    expect(outcomes).toEqual([{ kind: "failed", key: "a", fileName: "slow.pdf", message: "Raffa.ai could not process slow.pdf. Try again." }]);
  });
});
