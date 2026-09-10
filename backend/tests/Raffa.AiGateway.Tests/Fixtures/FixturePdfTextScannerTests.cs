using System.IO.Compression;
using System.Text;
using Raffa.AiGateway.Fixtures;

namespace Raffa.AiGateway.Tests.Fixtures;

/// <summary>
/// Proves <see cref="FixturePdfTextScanner"/> — the fixture `ocr` role's reader for the repo's
/// hand-built PDFs (moved here from the retired native PDF path, ADR-017 amendment 2026-09-09).
/// Hand-built byte fixtures are safe: this scanner never reads a cross-reference table or object
/// numbering, only the literal markers these fixtures place.
/// </summary>
public sealed class FixturePdfTextScannerTests
{
    [Fact]
    public void Pairs_pages_with_their_text_streams_in_file_order()
    {
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Length 100 >>\n" +
            "stream\n" +
            "BT (This is the first page of a real contract with plenty of readable text.) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "3 0 obj << /Type /Page >> endobj\n" +
            "4 0 obj << /Length 100 >>\n" +
            "stream\n" +
            "BT (This is the second page, also containing plenty of readable contract text.) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";

        var pages = FixturePdfTextScanner.TryExtractPages(Encoding.Latin1.GetBytes(pdf));

        Assert.NotNull(pages);
        Assert.Equal(2, pages.Count);
        Assert.Contains("first page", pages[0], StringComparison.Ordinal);
        Assert.Contains("second page", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_with_no_text_operators_cannot_be_paired()
    {
        // No BT/ET anywhere in the content stream — an image-only ("scanned") page paints its
        // content via an XObject `Do` operator, never a text-showing operator.
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Length 20 >>\n" +
            "stream\n" +
            "/Im0 Do\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";

        Assert.Null(FixturePdfTextScanner.TryExtractPages(Encoding.Latin1.GetBytes(pdf)));
    }

    [Fact]
    public void More_pages_than_text_streams_is_null_rather_than_guessed()
    {
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Type /Page >> endobj\n" +
            "3 0 obj << /Length 60 >>\n" +
            "stream\n" +
            "BT (Only one content stream for two declared pages.) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";

        Assert.Null(FixturePdfTextScanner.TryExtractPages(Encoding.Latin1.GetBytes(pdf)));
    }

    [Fact]
    public void Garbage_bytes_are_null_not_a_crash()
    {
        byte[] garbage = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x10, 0x20];

        Assert.Null(FixturePdfTextScanner.TryExtractPages(garbage));
    }

    [Fact]
    public void A_flate_compressed_content_stream_is_inflated_and_read()
    {
        const string pageText = "Compressed page text should still be extracted correctly by this scanner.";
        var compressed = ZlibCompress(Encoding.Latin1.GetBytes($"BT ({pageText}) Tj ET"));
        var compressedAsLatin1 = Encoding.Latin1.GetString(compressed);

        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            $"2 0 obj << /Filter /FlateDecode /Length {compressed.Length} >>\n" +
            "stream\n" +
            compressedAsLatin1 +
            "\nendstream\n" +
            "endobj\n" +
            "%%EOF\n";

        var pages = FixturePdfTextScanner.TryExtractPages(Encoding.Latin1.GetBytes(pdf));

        Assert.NotNull(pages);
        Assert.Contains("Compressed page text", Assert.Single(pages), StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_filter_stream_is_skipped_not_scanned_as_garbage()
    {
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Filter /DCTDecode /Length 10 >>\n" +
            "stream\n" +
            "ÿØÿàbinary\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";

        Assert.Null(FixturePdfTextScanner.TryExtractPages(Encoding.Latin1.GetBytes(pdf)));
    }

    [Fact]
    public void Escaped_literals_are_unescaped()
    {
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Length 60 >>\n" +
            "stream\n" +
            "BT (Fees \\(EUR\\) are due\\nquarterly) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";

        var pages = FixturePdfTextScanner.TryExtractPages(Encoding.Latin1.GetBytes(pdf));

        Assert.NotNull(pages);
        Assert.Equal("Fees (EUR) are due\nquarterly", Assert.Single(pages));
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
