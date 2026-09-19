using System.Buffers.Binary;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Raffa.AiGateway.Configuration;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Preview;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using WP = DocumentFormat.OpenXml.Wordprocessing;

namespace Raffa.Documents.Contracts.Tests.Preview;

/// <summary>
/// Tasks E13/F04/US01/T02 (R-DOC-08) and E22/F02/US01/T01 (ADR-029):
/// <list type="bullet">
/// <item>E13: the built-in renderer really produces a PNG a browser can decode — signature, IHDR
/// geometry, IEND — and takes the honest branches: a PNG upload is its own preview, everything else
/// gets a typed placeholder.</item>
/// <item>E22: pages render one at a time; a throwing renderer degrades to "no preview"; the reap
/// deletes only <c>n &gt; pageCount</c> and only under this document's prefix; a null or zero
/// <c>pageCount</c> deletes nothing.</item>
/// </list>
/// </summary>
public sealed class DocumentPreviewRenderingTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly PlaceholderDocumentPreviewRenderer _renderer = new();

    [Fact]
    public void Png_upload_is_its_own_preview()
    {
        byte[] uploaded = [.. PngSignature, 0x01, 0x02, 0x03];

        var preview = _renderer.Render("scan.png", DocumentFormatSniffer.PngMimeType, uploaded);

        Assert.Equal(uploaded, preview);
    }

    [Theory]
    [InlineData(DocumentFormatSniffer.PdfMimeType, "contract.pdf")]
    [InlineData(DocumentFormatSniffer.DocxMimeType, "contract.docx")]
    [InlineData(DocumentFormatSniffer.XlsxMimeType, "prices.xlsx")]
    [InlineData(DocumentFormatSniffer.JpegMimeType, "scan.jpg")]
    public void Every_other_format_gets_a_decodable_placeholder_png(string mimeType, string fileName)
    {
        var preview = _renderer.Render(fileName, mimeType, new byte[] { 1, 2, 3 });

        Assert.NotNull(preview);
        AssertIsPng(preview!);
    }

    [Fact]
    public void Placeholders_differ_per_format_so_the_card_says_something_true()
    {
        var pdf = _renderer.Render("a.pdf", DocumentFormatSniffer.PdfMimeType, new byte[] { 1 })!;
        var docx = _renderer.Render("a.docx", DocumentFormatSniffer.DocxMimeType, new byte[] { 1 })!;

        Assert.NotEqual(pdf, docx);
    }

    [Fact]
    public void Rendering_is_deterministic()
    {
        var first = _renderer.Render("a.pdf", DocumentFormatSniffer.PdfMimeType, new byte[] { 1 })!;
        var second = _renderer.Render("b.pdf", DocumentFormatSniffer.PdfMimeType, new byte[] { 9, 9 })!;

        Assert.Equal(first, second);
    }

    [Fact]
    public void An_unknown_mime_type_falls_back_to_the_extension_label()
    {
        var preview = _renderer.Render("mystery.bin", "application/octet-stream", new byte[] { 1 });

        Assert.NotNull(preview);
        AssertIsPng(preview!);
    }

    [Fact]
    public void Png_writer_encodes_the_declared_geometry_and_a_terminating_chunk()
    {
        var image = new PngImage(12, 7, new Rgb(0x10, 0x20, 0x30));
        image.Fill(2, 2, 4, 3, new Rgb(0xFF, 0x00, 0x00));
        image.Outline(0, 0, 12, 7, 1, new Rgb(0x00, 0x00, 0x00));
        image.DrawText("PDF 1", 1, 1, 1, new Rgb(0xFF, 0xFF, 0xFF));

        var png = image.ToPng();

        AssertIsPng(png);
        Assert.Equal(12, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        Assert.Equal(8, png[24]);  // bit depth
        Assert.Equal(2, png[25]);  // colour type: truecolour
    }

    [Fact]
    public void Measure_text_matches_what_drawing_occupies()
    {
        Assert.Equal(0, PngImage.MeasureText(string.Empty, 4));
        Assert.Equal(20, PngImage.MeasureText("A", 4));
        Assert.Equal(44, PngImage.MeasureText("AB", 4));
    }

    [Fact]
    public void Transparent_pdfium_pixels_composite_onto_white_not_opaque_black()
    {
        // pdfium leaves an unspecified page background as BGRA (0,0,0,0). Dropping alpha
        // used to encode that as an opaque black PNG — black text on a black page.
        var bgra = new byte[]
        {
            0, 0, 0, 0,          // fully transparent → white
            0, 0, 0, 255,        // opaque black text → black
            0, 0, 255, 255,      // opaque red (BGRA) → red
            0, 0, 0, 128,        // half-alpha black → mid grey
        };

        var rgb = PngImage.BgraToRgbOnWhite(2, 2, bgra);

        Assert.Equal([255, 255, 255], rgb[0..3]);
        Assert.Equal([0, 0, 0], rgb[3..6]);
        Assert.Equal([255, 0, 0], rgb[6..9]);
        Assert.Equal([127, 127, 127], rgb[9..12]);
    }

    private static void AssertIsPng(byte[] bytes)
    {
        Assert.True(bytes.Length > 8, "A PNG needs more than its signature.");
        Assert.Equal(PngSignature, bytes[..8]);
        Assert.Equal("IHDR"u8.ToArray(), bytes[12..16]);
        Assert.Equal("IEND"u8.ToArray(), bytes[^8..^4]);
    }

    // ---- Task E22/F02/US01/T01 (ADR-029 clause 3-4 / round-3 clause 1-2) ----

    [Fact]
    public void Office_renderer_paints_docx_page_text_not_the_file_placeholder()
    {
        var renderer = new OfficePageDocumentPreviewRenderer();
        var bytes = BuildMinimalDocx("IBM Enterprise Service Order Form");
        var placeholder = PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE");

        var preview = renderer.Render("order.docx", DocumentFormatSniffer.DocxMimeType, bytes, page: 1);

        Assert.NotNull(preview);
        AssertIsPng(preview!);
        Assert.NotEqual(placeholder, preview);
        Assert.Equal(OfficePageDocumentPreviewRenderer.PageWidth, BinaryPrimitives.ReadInt32BigEndian(preview.AsSpan(16, 4)));
        Assert.True(BinaryPrimitives.ReadInt32BigEndian(preview.AsSpan(20, 4)) >= OfficePageDocumentPreviewRenderer.PageHeight);
    }

    [Fact]
    public void Office_renderer_returns_null_for_a_page_past_the_docx_page_map()
    {
        var renderer = new OfficePageDocumentPreviewRenderer();
        var bytes = BuildMinimalDocx("One page only");

        Assert.Null(renderer.Render("order.docx", DocumentFormatSniffer.DocxMimeType, bytes, page: 2));
    }

    [Fact]
    public void Composite_renderer_uses_office_painter_when_pdfium_has_nothing()
    {
        var composite = new CompositeDocumentPreviewRenderer(
            new PdfPageDocumentPreviewRenderer(),
            new ImageDocumentPreviewRenderer(),
            new OfficePageDocumentPreviewRenderer());
        var bytes = BuildMinimalDocx("Supplier IBM Corporation");

        var preview = composite.Render("order.docx", DocumentFormatSniffer.DocxMimeType, bytes);

        Assert.NotNull(preview);
        AssertIsPng(preview!);
        Assert.NotEqual(PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE"), preview);
    }

    [Fact]
    public void Pdf_page_renderer_does_not_unload_pdfium_between_calls()
    {
        // DocLib.Instance is a process-wide singleton. `using` it after the first page
        // calls FPDF_DestroyLibrary; the next LoadMemDocument then access-violates
        // (0xC0000005) — a managed catch cannot absorb that, and the unfiltered
        // slnx run aborts. Two sequential calls must both complete.
        var renderer = new PdfPageDocumentPreviewRenderer();
        var bytes = "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n%%EOF\n"u8.ToArray();

        var first = renderer.Render("a.pdf", DocumentFormatSniffer.PdfMimeType, bytes);
        var second = renderer.Render("a.pdf", DocumentFormatSniffer.PdfMimeType, bytes);

        Assert.Equal(first is null, second is null);
        if (first is not null)
        {
            AssertIsPng(first);
            AssertIsPng(second!);
        }
    }

    [Fact]
    public async Task Service_stores_one_page_per_call_and_returns_the_page1_path()
    {
        // ADR-005 w17 §19: never materialise a document's pages as a set.
        // ADR-029 clause 4: one SavePreviewPageAsync per page, bitmap discarded between calls.
        var storage = new RecordingStorage();
        var renderer = new PageCountingRenderer();
        var service = BuildService(storage, renderer);

        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        var page1Path = await service.RenderAndStoreAsync(
            tenantId, documentId, "contract.pdf", "application/pdf",
            new byte[] { 1, 2, 3 }, pageCount: 3);

        Assert.NotNull(page1Path);
        Assert.EndsWith("/preview/page-1.png", page1Path, StringComparison.Ordinal);
        Assert.Equal(3, storage.Saved.Count);
        Assert.Equal(3, renderer.PagesRendered.Count);
        // Each page is rendered once, in order.
        Assert.Equal([1, 2, 3], renderer.PagesRendered);
    }

    [Fact]
    public async Task A_throwing_renderer_leaves_PreviewPath_unset_and_does_not_fail_the_document()
    {
        // ADR-029 clause 3: a renderer that throws must not take an admitted upload down.
        var storage  = new RecordingStorage();
        var service  = BuildService(storage, new ThrowingRenderer());

        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        var result = await service.RenderAndStoreAsync(
            tenantId, documentId, "contract.pdf", "application/pdf",
            new byte[] { 1, 2 }, pageCount: 1);

        Assert.Null(result);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task Reap_deletes_pages_beyond_the_new_pageCount_when_previous_was_larger()
    {
        // ADR-029 round-3 clause 2: a shorter reprocess deletes page-{n}.png for n > pageCount.
        var storage  = new RecordingStorage();
        var renderer = new PageCountingRenderer();
        var service  = BuildService(storage, renderer);

        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        // Simulate: first run produced 5 pages.
        for (var p = 1; p <= 5; p++)
        {
            var bytes = PlaceholderDocumentPreviewRenderer.RenderPlaceholder($"P{p}");
            using var s = new MemoryStream(bytes, writable: false);
            await storage.SavePreviewPageAsync(tenantId, documentId, p, s);
        }

        storage.Saved.Clear();

        // Reprocess with only 3 pages → pages 4 and 5 must be deleted.
        await service.RenderAndStoreAsync(
            tenantId, documentId, "contract.pdf", "application/pdf",
            new byte[] { 1 }, pageCount: 3, previousPageCount: 5);

        // 3 new pages saved + 2 old pages deleted.
        Assert.Equal(3, storage.Saved.Count);
        Assert.Equal(2, storage.Deleted.Count);
        Assert.All(storage.Deleted, p => Assert.Contains("/preview/page-", p, StringComparison.Ordinal));
        Assert.Contains(storage.Deleted, p => p.EndsWith("/page-4.png", StringComparison.Ordinal));
        Assert.Contains(storage.Deleted, p => p.EndsWith("/page-5.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reap_deletes_nothing_when_pageCount_is_zero_or_below()
    {
        // ADR-029 round-3 clause 2: "reap everything above 0" is the arithmetic that reaps
        // the document; a zero or negative pageCount deletes nothing.
        var storage = new RecordingStorage();
        var service = BuildService(storage, new PageCountingRenderer());

        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        await service.RenderAndStoreAsync(
            tenantId, documentId, "contract.pdf", "application/pdf",
            new byte[] { 1 }, pageCount: 0, previousPageCount: 5);

        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task Reap_paths_are_under_this_document_prefix_only_never_another_document()
    {
        // ADR-009 w17 clause 8: the reap is bounded by (tenant, document) — never an
        // enumeration of the tenant prefix, which EnsureWithinTenant would not catch
        // (same tenant, wrong document, guard silent).
        var storage   = new RecordingStorage();
        var renderer  = new PageCountingRenderer();
        var service   = BuildService(storage, renderer);
        var tenantId  = TenantId.New();
        var docA      = EntityId.New();
        var docB      = EntityId.New();

        // docB has 5 pages already "in storage".
        for (var p = 1; p <= 5; p++)
        {
            using var s = new MemoryStream(PlaceholderDocumentPreviewRenderer.RenderPlaceholder($"P{p}"), writable: false);
            await storage.SavePreviewPageAsync(tenantId, docB, p, s);
        }

        storage.Saved.Clear();

        // Reprocessing docA (3 pages, was 5) must NOT delete anything from docB's prefix.
        await service.RenderAndStoreAsync(
            tenantId, docA, "a.pdf", "application/pdf",
            new byte[] { 1 }, pageCount: 3, previousPageCount: 5);

        foreach (var deleted in storage.Deleted)
        {
            // Every deleted path must contain docA's id, never docB's.
            Assert.Contains(docA.Value.ToString("D"), deleted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(docB.Value.ToString("D"), deleted, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Budget_cap_limits_rendered_pages_and_leaves_surplus_pages_unrendered()
    {
        // ADR-029 clause 7: pages beyond MaxPagesPerDocument are not rendered, and the
        // read model says so (IsPageCountLimited).
        var storage   = new RecordingStorage();
        var renderer  = new PageCountingRenderer();
        var ocrOptions = new AiGatewayOcrOptions { MaxPagesPerDocument = 2 };
        var service   = BuildService(storage, renderer, ocrOptions);

        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        await service.RenderAndStoreAsync(
            tenantId, documentId, "big.pdf", "application/pdf",
            new byte[] { 1 }, pageCount: 10); // 10 pages, cap = 2

        Assert.Equal(2, storage.Saved.Count);
        Assert.Equal([1, 2], renderer.PagesRendered);
    }

    // ---- helpers ----

    private static byte[] BuildMinimalDocx(string text)
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new WP.Document(new WP.Body(new WP.Paragraph(new WP.Run(new WP.Text(text)))));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static DocumentPreviewService BuildService(
        RecordingStorage storage,
        IDocumentPreviewRenderer renderer,
        AiGatewayOcrOptions? ocrOptions = null)
    {
        var dbCtxOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbCtx = new DocumentsContractsDbContext(dbCtxOptions);
        return new DocumentPreviewService(dbCtx, storage, renderer, new NullTenantContext(), ocrOptions);
    }

    /// <summary>Renderer that records which pages were requested, returning a placeholder PNG for each.</summary>
    private sealed class PageCountingRenderer : IDocumentPreviewRenderer
    {
        public List<int> PagesRendered { get; } = [];

        public byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content, int page = 1)
        {
            PagesRendered.Add(page);
            return PlaceholderDocumentPreviewRenderer.RenderPlaceholder($"P{page}");
        }
    }

    private sealed class ThrowingRenderer : IDocumentPreviewRenderer
    {
        public byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content, int page = 1) =>
            throw new InvalidOperationException("Simulated renderer crash.");
    }

    private sealed class RecordingStorage : IDocumentStorage
    {
        public List<(string Path, byte[] Content)> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public async Task<string> SaveAsync(TenantId t, EntityId d, int v, string f, Stream s, CancellationToken ct = default)
            => await StoreAsync(DocumentStoragePath.Build(t, d, v, f), s, ct);

        public async Task<string> SavePreviewAsync(TenantId t, EntityId d, Stream s, CancellationToken ct = default)
            => await StoreAsync(DocumentStoragePath.BuildPreview(t, d), s, ct);

        public async Task<string> SavePreviewPageAsync(TenantId t, EntityId d, int p, Stream s, CancellationToken ct = default)
            => await StoreAsync(DocumentStoragePath.BuildPreviewPage(t, d, p), s, ct);

        public Task<byte[]?> LoadAsync(TenantId t, string path, CancellationToken ct = default)
        {
            DocumentStoragePath.EnsureWithinTenant(t, path);
            return Task.FromResult(_objects.TryGetValue(path, out var b) ? (byte[]?)b : null);
        }

        public Task DeleteAsync(TenantId t, string path, CancellationToken ct = default)
        {
            DocumentStoragePath.EnsureWithinTenant(t, path);
            Deleted.Add(path);
            _objects.Remove(path);
            return Task.CompletedTask;
        }

        private async Task<string> StoreAsync(string path, Stream s, CancellationToken ct)
        {
            using var buf = new MemoryStream();
            await s.CopyToAsync(buf, ct);
            var bytes = buf.ToArray();
            Saved.Add((path, bytes));
            _objects[path] = bytes;
            return path;
        }
    }

    private sealed class NullTenantContext : ITenantContext
    {
        public TenantId? Current => null;
        public IDisposable BeginScope(TenantId tenantId) => new NullScope();
        private sealed class NullScope : IDisposable { public void Dispose() { } }
    }
}
