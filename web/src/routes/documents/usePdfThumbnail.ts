import { useEffect, useState } from "react";

/**
 * Renders the first page of a PDF `File` to a small canvas thumbnail via PDF.js (loaded lazily via
 * dynamic import so the ~1 MB library stays out of the main bundle). Returns a JPEG data URL once
 * ready, or `null` while loading / for non-PDF files / on any render error.
 *
 * Plan: instant-upload-open — "Thumbnail first page via PDF.js in the browser."
 *
 * Usage: call this hook once per local upload row; each call mounts its own effect and cancels it
 * on unmount, so a batch of 20 files renders thumbnails concurrently without blocking each other.
 */

/** Target width in CSS pixels. Height is derived from the page's own aspect ratio. */
const THUMBNAIL_WIDTH_PX = 72;

function isPdf(file: File): boolean {
  return file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf");
}

export function usePdfThumbnail(file: File): string | null {
  const [dataUrl, setDataUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!isPdf(file)) return;

    let cancelled = false;

    async function render(): Promise<void> {
      try {
        // Dynamic import — PDF.js is large; keep it out of the initial JS bundle.
        const pdfjsLib = await import("pdfjs-dist");

        if (cancelled) return;

        // Point at the bundled worker via Vite's `?url` import so there is no CDN dependency.
        // (Vite rewrites `new URL(..., import.meta.url)` at build time.)
        pdfjsLib.GlobalWorkerOptions.workerSrc = new URL(
          "pdfjs-dist/build/pdf.worker.min.mjs",
          import.meta.url,
        ).toString();

        const buffer = await file.arrayBuffer();
        if (cancelled) return;

        const pdf = await pdfjsLib.getDocument({ data: buffer }).promise;
        if (cancelled) return;

        const page = await pdf.getPage(1);
        if (cancelled) return;

        const nativeViewport = page.getViewport({ scale: 1 });
        const scale = THUMBNAIL_WIDTH_PX / nativeViewport.width;
        const viewport = page.getViewport({ scale });

        const canvas = document.createElement("canvas");
        canvas.width = Math.round(viewport.width);
        canvas.height = Math.round(viewport.height);

        const ctx = canvas.getContext("2d");
        if (!ctx) return;

        await page.render({ canvasContext: ctx, viewport }).promise;
        if (cancelled) return;

        setDataUrl(canvas.toDataURL("image/jpeg", 0.7));
      } catch {
        // Silently swallow: thumbnail is a best-effort enhancement, never load-bearing.
      }
    }

    void render();
    return () => {
      cancelled = true;
    };
  }, [file]);

  return dataUrl;
}
