using System.Buffers.Binary;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Preview;

namespace Raffa.Documents.Contracts.Tests.Preview;

/// <summary>
/// Task E13/F04/US01/T02 (R-DOC-08): the built-in renderer really produces a PNG a browser can
/// decode — signature, IHDR geometry, IEND — and takes the honest branches: a PNG upload is its own
/// preview, everything else gets a typed placeholder.
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

    private static void AssertIsPng(byte[] bytes)
    {
        Assert.True(bytes.Length > 8, "A PNG needs more than its signature.");
        Assert.Equal(PngSignature, bytes[..8]);
        Assert.Equal("IHDR"u8.ToArray(), bytes[12..16]);
        Assert.Equal("IEND"u8.ToArray(), bytes[^8..^4]);
    }
}
