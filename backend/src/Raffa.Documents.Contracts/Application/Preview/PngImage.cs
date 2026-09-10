using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// A tiny, dependency-free 8-bit RGB raster plus a PNG encoder (task E13/F04/US01/T02, R-DOC-08).
///
/// <para>
/// <b>Why hand-rolled.</b> The only thing this module has to draw is the honest placeholder the
/// requirement itself asks for ("DOCX / XLSX get an honest placeholder"). Pulling a native
/// imaging stack (SkiaSharp/pdfium) into a domain module to paint a rectangle and a label would
/// contradict ADR-002 (no provider SDK inside a domain module) and would put platform-specific
/// native assets on every build agent and container image. Everything here is managed code from
/// the base class library: <see cref="ZLibStream"/> supplies PNG's zlib-wrapped deflate, and the
/// CRC-32 is the 30-line one from the PNG specification. Output is byte-for-byte deterministic,
/// which is what makes the preview testable.
/// </para>
///
/// <para>
/// A real first-page raster of a PDF needs a PDF rasteriser and is therefore NOT done here — see
/// <see cref="IDocumentPreviewRenderer"/> for the seam a pdfium/Skia-backed renderer plugs into
/// inside <c>Raffa.Api</c> without any caller changing.
/// </para>
/// </summary>
public sealed class PngImage
{
    private readonly byte[] _pixels;

    public PngImage(int width, int height, Rgb background)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        _pixels = new byte[width * height * 3];
        Fill(0, 0, width, height, background);
    }

    public int Width { get; }

    public int Height { get; }

    public void Fill(int x, int y, int width, int height, Rgb colour)
    {
        var right = Math.Min(x + width, Width);
        var bottom = Math.Min(y + height, Height);

        for (var row = Math.Max(y, 0); row < bottom; row++)
        {
            for (var column = Math.Max(x, 0); column < right; column++)
            {
                var offset = ((row * Width) + column) * 3;
                _pixels[offset] = colour.R;
                _pixels[offset + 1] = colour.G;
                _pixels[offset + 2] = colour.B;
            }
        }
    }

    /// <summary>Unfilled rectangle, <paramref name="thickness"/> pixels wide.</summary>
    public void Outline(int x, int y, int width, int height, int thickness, Rgb colour)
    {
        Fill(x, y, width, thickness, colour);
        Fill(x, y + height - thickness, width, thickness, colour);
        Fill(x, y, thickness, height, colour);
        Fill(x + width - thickness, y, thickness, height, colour);
    }

    /// <summary>
    /// Draws <paramref name="text"/> with the built-in 5x7 bitmap font, each font pixel scaled to
    /// a <paramref name="scale"/>-square block. Unsupported characters render as blanks — a
    /// placeholder never fails, it just says less.
    /// </summary>
    public void DrawText(string text, int x, int y, int scale, Rgb colour)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);

        var cursorX = x;
        foreach (var character in text)
        {
            var glyph = BitmapFont5x7.Glyph(character);
            for (var row = 0; row < BitmapFont5x7.GlyphHeight; row++)
            {
                for (var column = 0; column < BitmapFont5x7.GlyphWidth; column++)
                {
                    if (glyph[row][column] == '1')
                    {
                        Fill(cursorX + (column * scale), y + (row * scale), scale, scale, colour);
                    }
                }
            }

            cursorX += (BitmapFont5x7.GlyphWidth + 1) * scale;
        }
    }

    /// <summary>Width in pixels <see cref="DrawText"/> will occupy — for centring a label.</summary>
    public static int MeasureText(string text, int scale) =>
        text.Length == 0 ? 0 : (((BitmapFont5x7.GlyphWidth + 1) * text.Length) - 1) * scale;

    /// <summary>Encodes the raster as a PNG (8-bit RGB, no interlacing, filter type 0).</summary>
    public byte[] ToPng()
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), Height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: truecolour (RGB)
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter method: adaptive
        header[12] = 0; // interlace: none
        WriteChunk(png, "IHDR", header);

        using var raw = new MemoryStream();
        for (var row = 0; row < Height; row++)
        {
            raw.WriteByte(0); // filter type 0 (None) for every scanline
            raw.Write(_pixels, row * Width * 3, Width * 3);
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(deflate);
        }

        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream target, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        target.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        target.Write(typeBytes);
        target.Write(data);

        var crc = Crc32.Compute(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        target.Write(crcBytes);
    }
}

/// <summary>An 8-bit-per-channel colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>CRC-32 as specified by the PNG format (ISO 3309 / ITU-T V.42 polynomial).</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var crc = 0xFFFFFFFFu;
        crc = Update(crc, first);
        crc = Update(crc, second);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

/// <summary>
/// A 5x7 uppercase bitmap font — enough to write a file-type label and a short line of English on
/// the placeholder preview. Rows are strings of <c>0</c>/<c>1</c> so the glyphs stay readable in
/// the source.
/// </summary>
internal static class BitmapFont5x7
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    private static readonly string[] Blank = ["00000", "00000", "00000", "00000", "00000", "00000", "00000"];

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['I'] = ["11111", "00100", "00100", "00100", "00100", "00100", "11111"],
        ['J'] = ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "11011", "10001"],
        ['X'] = ["10001", "01010", "00100", "00100", "00100", "01010", "10001"],
        ['Y'] = ["10001", "01010", "00100", "00100", "00100", "00100", "00100"],
        ['Z'] = ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
        ['3'] = ["11111", "00010", "00100", "00010", "00001", "10001", "01110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
        ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
        ['.'] = ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
        ['/'] = ["00001", "00010", "00010", "00100", "01000", "01000", "10000"],
        [' '] = ["00000", "00000", "00000", "00000", "00000", "00000", "00000"],
    };

    public static string[] Glyph(char character) =>
        Glyphs.TryGetValue(char.ToUpperInvariant(character), out var glyph) ? glyph : Blank;
}
