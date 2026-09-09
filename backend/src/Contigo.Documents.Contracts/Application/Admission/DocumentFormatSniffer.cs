using System.IO.Compression;

namespace Contigo.Documents.Contracts.Application.Admission;

/// <summary>The five container formats Contigo reads (<c>inputs/requirements.md</c> R-DOC-02). Named
/// <c>Uploaded…</c> because <c>DocumentFormat</c> alone would collide with the OpenXml SDK's own root
/// namespace, which this same module imports (<see cref="Extraction.NativeDocumentTextExtractor"/>).</summary>
public enum UploadedDocumentFormat
{
    Pdf,
    Docx,
    Xlsx,
    Png,
    Jpeg,
}

/// <summary>
/// What <see cref="DocumentFormatSniffer.TryDetect"/> established about an upload: the format,
/// the canonical MIME type the rest of the pipeline should use for it (never the browser's own
/// <c>Content-Type</c>, which is unverified and often <c>application/octet-stream</c>), and the
/// lower-cased extension it was checked against.
/// </summary>
public sealed record DocumentFormatDetection(UploadedDocumentFormat Format, string MimeType, string Extension);

/// <summary>
/// Task E13/F04/US01/T01 (documents-admission): the format check <c>POST /api/documents</c> runs
/// first — by extension <b>and</b> magic bytes, before any parse or model call (R-DOC-02: "Format
/// is checked by extension and magic bytes before any model call"; AC-1 "a <c>.zip</c> renamed
/// <c>.pdf</c> is refused (415) without touching the AI gateway").
///
/// <para>
/// <b>Both must agree.</b> The extension names the format the caller <em>claims</em>; the bytes
/// must carry that format's own signature — <c>%PDF-</c> in the first 1 KiB (ISO 32000-1 §7.5.2
/// plus the leading-junk tolerance every real reader has), the PNG 8-byte signature, the JPEG
/// SOI marker, or a ZIP whose central directory holds the OOXML main part
/// (<c>word/document.xml</c> for Word, <c>xl/workbook.xml</c> for Excel — exactly the parts
/// <c>DocumentFormat.OpenXml</c> opens later). A PNG renamed <c>.pdf</c> is therefore refused,
/// not silently re-labelled: the caller said one thing and sent another, and the gate's whole
/// point is to trust neither claim on its own. The declared <c>Content-Type</c> is not consulted
/// at all.
/// </para>
///
/// <para>
/// <b>Why TIFF/GIF/BMP/HEIC are not here</b>: R-DOC-02 lists exactly PDF, DOCX, XLSX, PNG and
/// JPEG; ADR-017's Document Intelligence path reads more, but widening the accepted set is a
/// requirements change, not an implementation choice.
/// </para>
/// </summary>
public static class DocumentFormatSniffer
{
    /// <summary>The HTTP 415 body, verbatim from R-DOC-02 and the OpenAPI <c>uploadDocument</c>
    /// 415 example (the web client quotes it; keep it byte-for-byte).</summary>
    public const string RejectionMessage = "Contigo reads PDF, Word, Excel and scanned images";

    public const string PdfMimeType = "application/pdf";
    public const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PngMimeType = "image/png";
    public const string JpegMimeType = "image/jpeg";

    /// <summary>How far into the file a <c>%PDF-</c> header may sit (ISO 32000-1 readers accept up
    /// to 1024 bytes of leading junk; Acrobat does the same).</summary>
    private const int PdfHeaderSearchWindow = 1024;

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] ZipLocalHeaderSignature = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Returns <see langword="true"/> with the detected format when the file name's extension and the
    /// content's signature agree on one of the five accepted formats; <see langword="false"/> for
    /// everything else (no extension, an unknown extension, a mismatch, or a container whose
    /// signature is right but whose payload is not that format — e.g. a plain <c>.zip</c> renamed
    /// <c>.docx</c>).
    /// </summary>
    public static bool TryDetect(string? fileName, ReadOnlySpan<byte> content, out DocumentFormatDetection detection)
    {
        detection = null!;
        if (string.IsNullOrWhiteSpace(fileName) || content.IsEmpty)
        {
            return false;
        }

        var extension = Path.GetExtension(fileName.Trim()).ToLowerInvariant();
        switch (extension)
        {
            case ".pdf" when LooksLikePdf(content):
                detection = new DocumentFormatDetection(UploadedDocumentFormat.Pdf, PdfMimeType, extension);
                return true;
            case ".docx" when LooksLikeOoxml(content, "word/document.xml"):
                detection = new DocumentFormatDetection(UploadedDocumentFormat.Docx, DocxMimeType, extension);
                return true;
            case ".xlsx" when LooksLikeOoxml(content, "xl/workbook.xml"):
                detection = new DocumentFormatDetection(UploadedDocumentFormat.Xlsx, XlsxMimeType, extension);
                return true;
            case ".png" when content.StartsWith(PngSignature):
                detection = new DocumentFormatDetection(UploadedDocumentFormat.Png, PngMimeType, extension);
                return true;
            case ".jpg" or ".jpeg" when content.StartsWith(JpegSignature):
                detection = new DocumentFormatDetection(UploadedDocumentFormat.Jpeg, JpegMimeType, extension);
                return true;
            default:
                return false;
        }
    }

    private static bool LooksLikePdf(ReadOnlySpan<byte> content)
    {
        var window = content.Length > PdfHeaderSearchWindow ? content[..PdfHeaderSearchWindow] : content;
        return window.IndexOf(PdfSignature) >= 0;
    }

    /// <summary>
    /// A ZIP local-file-header signature is necessary but not sufficient — every OOXML file is a
    /// ZIP, but so is a renamed archive. The central directory (what <see cref="ZipArchive"/> reads
    /// on open, without inflating anything) must name the package's main part.
    /// </summary>
    private static bool LooksLikeOoxml(ReadOnlySpan<byte> content, string mainPartName)
    {
        if (!content.StartsWith(ZipLocalHeaderSignature))
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            return archive.Entries.Any(entry =>
                string.Equals(entry.FullName, mainPartName, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
