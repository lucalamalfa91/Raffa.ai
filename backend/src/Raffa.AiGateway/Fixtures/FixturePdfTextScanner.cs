using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Raffa.AiGateway.Fixtures;

/// <summary>
/// The lightweight PDF content-stream scan that used to be
/// <c>Raffa.Documents.Contracts.Application.Extraction.NativeDocumentTextExtractor</c>'s "native"
/// PDF path, kept here as the fixture <c>ocr</c> role's reader for born-digital PDFs (ADR-017
/// amendment 2026-09-09: in production every PDF goes through Document Intelligence; the fixture
/// gateway must still turn the hand-built test PDFs this repo ships into page text without any
/// provider, so CI stays offline). Deliberately not a full PDF parser: it counts <c>/Type /Page</c>
/// objects, decodes uncompressed or <c>/FlateDecode</c> streams, keeps the ones with a
/// <c>BT ... ET</c> text object, pairs them 1:1 with the pages in file order, and extracts literal
/// string operands. Anything it cannot pair (object streams, hex strings, CID fonts, scanned pages)
/// is reported as <see langword="null"/> so the caller can fall back to its honest placeholder.
/// </summary>
public static class FixturePdfTextScanner
{
    /// <summary>How far back from a <c>stream</c> keyword the scan looks for that stream object's
    /// own dictionary (to check for <c>/FlateDecode</c>).</summary>
    private const int DictionaryLookbackWindow = 1000;

    private static readonly Regex PageObjectRegex = new(
        @"(?<![A-Za-z])/Type\s*/Page(?![A-Za-z])", RegexOptions.Compiled);

    private static readonly Regex TextObjectRegex = new(
        @"BT(?<body>.*?)ET", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex LiteralStringRegex = new(
        @"\((?<text>(?:[^()\\]|\\.)*)\)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// One text string per page, in page order, or <see langword="null"/> when the bytes do not
    /// declare any page or the text-bearing content streams cannot be paired 1:1 with the pages.
    /// </summary>
    public static IReadOnlyList<string>? TryExtractPages(ReadOnlySpan<byte> content)
    {
        // Latin-1 maps every byte value 0-255 to exactly one char and back losslessly, so slicing
        // this string and re-encoding a slice to bytes never corrupts binary stream content.
        var raw = Encoding.Latin1.GetString(content);

        var pageCount = PageObjectRegex.Count(raw);
        if (pageCount == 0)
        {
            return null;
        }

        var streamTexts = FindTextContentStreams(raw);
        return streamTexts.Count == pageCount ? streamTexts : null;
    }

    private static List<string> FindTextContentStreams(string raw)
    {
        var texts = new List<string>();
        var searchFrom = 0;

        while (true)
        {
            var streamKeywordIndex = raw.IndexOf("stream", searchFrom, StringComparison.Ordinal);
            if (streamKeywordIndex < 0)
            {
                break;
            }

            var bodyStart = streamKeywordIndex + "stream".Length;
            if (bodyStart < raw.Length && raw[bodyStart] == '\r')
            {
                bodyStart++;
            }

            if (bodyStart < raw.Length && raw[bodyStart] == '\n')
            {
                bodyStart++;
            }

            var endStreamIndex = raw.IndexOf("endstream", bodyStart, StringComparison.Ordinal);
            if (endStreamIndex < 0)
            {
                break;
            }

            var lookbackStart = Math.Max(0, streamKeywordIndex - DictionaryLookbackWindow);
            var dictionary = raw[lookbackStart..streamKeywordIndex];
            var body = raw[bodyStart..endStreamIndex];

            var decodedBody = TryDecodeStreamBody(dictionary, body);
            if (decodedBody is not null && TextObjectRegex.IsMatch(decodedBody))
            {
                texts.Add(ExtractTextShowingOperators(decodedBody));
            }

            searchFrom = endStreamIndex + "endstream".Length;
        }

        return texts;
    }

    private static string? TryDecodeStreamBody(string dictionary, string body)
    {
        var isFlateEncoded = dictionary.Contains("/FlateDecode", StringComparison.Ordinal);
        var hasOtherFilter = !isFlateEncoded && dictionary.Contains("/Filter", StringComparison.Ordinal);

        if (hasOtherFilter)
        {
            // An image codec (/DCTDecode, /CCITTFaxDecode, /JPXDecode ...) — never text.
            return null;
        }

        if (!isFlateEncoded)
        {
            return body;
        }

        try
        {
            var compressedBytes = Encoding.Latin1.GetBytes(body);
            using var compressed = new MemoryStream(compressedBytes);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            zlib.CopyTo(decompressed);

            return Encoding.Latin1.GetString(decompressed.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string ExtractTextShowingOperators(string streamText)
    {
        var builder = new StringBuilder();

        foreach (Match textObject in TextObjectRegex.Matches(streamText))
        {
            foreach (Match literal in LiteralStringRegex.Matches(textObject.Groups["body"].Value))
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(UnescapeLiteralString(literal.Groups["text"].Value));
            }
        }

        return builder.ToString();
    }

    private static string UnescapeLiteralString(string escaped)
    {
        var builder = new StringBuilder(escaped.Length);

        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] != '\\' || i == escaped.Length - 1)
            {
                builder.Append(escaped[i]);
                continue;
            }

            var next = escaped[i + 1];
            builder.Append(next switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => next,
            });
            i++;
        }

        return builder.ToString();
    }
}
