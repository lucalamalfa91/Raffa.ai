using System.Text;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// A structurally valid PDF pdfium will open (catalog, page tree, Helvetica, xref). The
/// hand-built <c>%PDF-1.4 /Type /Page</c> scraps used by the fixture scanner are not a real
/// file — they are what w19's OCR-only path still reads. These bytes prove the restored
/// native path.
/// </summary>
internal static class BornDigitalPdf
{
    public const string MsaPageOne =
        "MASTER SERVICES AGREEMENT between Acme Corp and IBM Corporation, effective 1 January 2026. " +
        "This Agreement governs all Order Forms executed by the parties. Annual spend is USD 188000. " +
        "Total contract value is USD 391000. Payment terms net 60. Auto-renewal applies.";

    public const string MsaPageTwo =
        "The initial term is thirty-six months and renews automatically unless either party gives " +
        "ninety days written notice. Start date 1 January 2026. End date 31 December 2028. " +
        "Cancellation deadline 2 October 2028. Governing law New York. Status active.";

    public static byte[] FromPages(params string[] pageTexts)
    {
        ArgumentOutOfRangeException.ThrowIfZero(pageTexts.Length);

        var pageCount = pageTexts.Length;
        var fontObject = 3 + (pageCount * 2);
        var pageObjectNumbers = Enumerable.Range(0, pageCount).Select(i => 3 + (i * 2)).ToArray();

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pageObjectNumbers.Select(n => $"{n} 0 R"))}] /Count {pageCount} >>",
        };

        foreach (var text in pageTexts)
        {
            var contentObject = objects.Count + 2;
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentObject} 0 R " +
                $"/Resources << /Font << /F1 {fontObject} 0 R >> >> >>");

            var stream = $"BT /F1 12 Tf 50 750 Td ({Escape(text)}) Tj ET\n";
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream");
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        var body = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(body.Length);
            body.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xrefAt = body.Length;
        body.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        body.Append("0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
        {
            body.Append(offsets[i].ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        body.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\n");
        body.Append("startxref\n").Append(xrefAt).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '(' or ')' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
