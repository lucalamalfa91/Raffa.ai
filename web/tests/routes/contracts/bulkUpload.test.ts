import { describe, expect, it } from "vitest";
import { runPool, UPLOAD_CONCURRENCY } from "../../../src/routes/contracts/useContractUpload";

describe("bulk contract upload", () => {
  it("uploads every file, never more than UPLOAD_CONCURRENCY at a time", async () => {
    const files = Array.from({ length: 100 }, (_, i) => `contract-${i}.pdf`);
    let inFlight = 0;
    let peak = 0;
    const seen: string[] = [];
    await runPool(files, UPLOAD_CONCURRENCY, async (f) => {
      inFlight++;
      peak = Math.max(peak, inFlight);
      await new Promise((r) => setTimeout(r, 1));
      seen.push(f);
      inFlight--;
    });
    expect(seen.sort()).toEqual([...files].sort());
    expect(peak).toBe(UPLOAD_CONCURRENCY);
  });

  it("handles fewer files than lanes", async () => {
    const seen: number[] = [];
    await runPool([1, 2], 4, async (n) => void seen.push(n));
    expect(seen.sort()).toEqual([1, 2]);
  });
});
