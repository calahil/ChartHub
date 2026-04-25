using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ChartHub.Conversion.Image;

/// <summary>
/// Decodes a Rock Band <c>.png_xbox</c> texture to a PNG byte array.
/// </summary>
/// <remarks>
/// This implementation mirrors Onyx <c>Onyx.Image.DXT.readRBImageMaybe</c> behavior for
/// HMX/GH version-1 image headers with Xbox DXT formats:
/// bitsPerPixel=0x04 + format=0x08 (DXT1), and bitsPerPixel=0x08 + format=0x18 (DXT3 color path).
/// </remarks>
internal static class PngXboxDecoder
{
    private const int HeaderSize = 0x20;
    private const byte VersionGh1AndLater = 0x01;
    private const byte BitsPerPixelDxt1 = 0x04;
    private const byte BitsPerPixelDxt3 = 0x08;
    private const uint FormatDxt1 = 0x08;
    private const uint FormatDxt3 = 0x18;

    /// <summary>Decodes a <c>.png_xbox</c> byte array and returns PNG-encoded bytes.</summary>
    public static byte[] Decode(byte[] rawBytes)
    {
        if (rawBytes.Length < HeaderSize)
        {
            throw new InvalidDataException("PNG Xbox texture header is too short.");
        }

        int offset = 0;
        byte version = ReadByte(rawBytes, ref offset);
        if (version != VersionGh1AndLater)
        {
            throw new InvalidDataException($"Unsupported HMX image version: 0x{version:X2}.");
        }

        byte bitsPerPixel = ReadByte(rawBytes, ref offset);
        uint format = ReadUInt32LE(rawBytes, ref offset);
        _ = ReadByte(rawBytes, ref offset); // mipmaps
        int width = ReadUInt16LE(rawBytes, ref offset);
        int height = ReadUInt16LE(rawBytes, ref offset);
        _ = ReadUInt16LE(rawBytes, ref offset); // bytesPerLine

        // Onyx validates 19 trailing zero bytes in the version-1 header.
        for (int i = 0; i < 19; i++)
        {
            if (ReadByte(rawBytes, ref offset) != 0)
            {
                throw new InvalidDataException("Invalid PNG Xbox header padding bytes.");
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("PNG Xbox texture dimensions are invalid.");
        }

        if ((width % 4) != 0 || (height % 4) != 0)
        {
            throw new InvalidDataException("PNG Xbox texture dimensions must be multiples of 4.");
        }

        int blocksWide = width / 4;
        int blocksHigh = height / 4;
        if (blocksWide <= 0 || blocksHigh <= 0)
        {
            throw new InvalidDataException("PNG Xbox texture block dimensions are invalid.");
        }

        byte[] rgba = (bitsPerPixel, format) switch
        {
            (BitsPerPixelDxt1, FormatDxt1) => DecodeDxtBlocks(rawBytes, offset, blocksWide, blocksHigh, isDxt1: true),
            // Onyx's PNGXbox DXT3 path skips alpha and decodes color like non-DXT1 interpolation.
            (BitsPerPixelDxt3, FormatDxt3) => DecodeDxtBlocks(rawBytes, offset, blocksWide, blocksHigh, isDxt1: false, skipAlphaBlock: true),
            _ => throw new InvalidDataException(
                $"Unsupported PNG Xbox format pair: bitsPerPixel=0x{bitsPerPixel:X2}, format=0x{format:X8}."),
        };

        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static byte[] DecodeDxtBlocks(
        byte[] rawBytes,
        int payloadOffset,
        int blocksWide,
        int blocksHigh,
        bool isDxt1,
        bool skipAlphaBlock = false)
    {
        int width = blocksWide * 4;
        int height = blocksHigh * 4;
        byte[] pixels = new byte[width * height * 4];

        int offset = payloadOffset;
        for (int blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (int blockX = 0; blockX < blocksWide; blockX++)
            {
                if (skipAlphaBlock)
                {
                    offset += 8;
                }

                ushort c0Raw = ReadUInt16BE(rawBytes, ref offset);
                ushort c1Raw = ReadUInt16BE(rawBytes, ref offset);
                (byte r0, byte g0, byte b0) = Rgb565ToRgb(c0Raw);
                (byte r1, byte g1, byte b1) = Rgb565ToRgb(c1Raw);

                (byte R, byte G, byte B) c2 = (!isDxt1 || c0Raw > c1Raw)
                    ? ((byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3))
                    : ((byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2));
                (byte R, byte G, byte B) c3 = (!isDxt1 || c0Raw > c1Raw)
                    ? ((byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3))
                    : ((byte)0, (byte)0, (byte)0);

                byte row0 = ReadByte(rawBytes, ref offset);
                byte row1 = ReadByte(rawBytes, ref offset);
                byte row2 = ReadByte(rawBytes, ref offset);
                byte row3 = ReadByte(rawBytes, ref offset);
                byte[] rows = [row0, row1, row2, row3];

                for (int py = 0; py < 4; py++)
                {
                    // Onyx PNGXbox path swaps rows in pairs: y' = y xor 1.
                    int sourceRow = py ^ 1;
                    byte rowBits = rows[sourceRow];

                    for (int px = 0; px < 4; px++)
                    {
                        bool bitHi = (rowBits & (1 << ((px * 2) + 1))) != 0;
                        bool bitLo = (rowBits & (1 << (px * 2))) != 0;

                        (byte R, byte G, byte B) color = (bitHi, bitLo) switch
                        {
                            (false, false) => (r0, g0, b0),
                            (false, true) => (r1, g1, b1),
                            (true, false) => c2,
                            (true, true) => c3,
                        };

                        int x = (blockX * 4) + px;
                        int y = (blockY * 4) + py;
                        int dest = ((y * width) + x) * 4;
                        pixels[dest] = color.R;
                        pixels[dest + 1] = color.G;
                        pixels[dest + 2] = color.B;
                        pixels[dest + 3] = 0xFF;
                    }
                }
            }
        }

        return pixels;
    }

    private static byte ReadByte(byte[] data, ref int offset)
    {
        if (offset >= data.Length)
        {
            throw new InvalidDataException("Unexpected end of PNG Xbox payload.");
        }

        return data[offset++];
    }

    private static ushort ReadUInt16LE(byte[] data, ref int offset)
    {
        byte b0 = ReadByte(data, ref offset);
        byte b1 = ReadByte(data, ref offset);
        return (ushort)(b0 | (b1 << 8));
    }

    private static uint ReadUInt32LE(byte[] data, ref int offset)
    {
        ushort low = ReadUInt16LE(data, ref offset);
        ushort high = ReadUInt16LE(data, ref offset);
        return (uint)(low | ((uint)high << 16));
    }

    private static ushort ReadUInt16BE(byte[] data, ref int offset)
    {
        byte b0 = ReadByte(data, ref offset);
        byte b1 = ReadByte(data, ref offset);
        return (ushort)((b0 << 8) | b1);
    }

    private static (byte r, byte g, byte b) Rgb565ToRgb(ushort v)
    {
        byte r = (byte)(((v >> 11) & 0x1F) * 255 / 31);
        byte g = (byte)(((v >> 5) & 0x3F) * 255 / 63);
        byte b = (byte)((v & 0x1F) * 255 / 31);
        return (r, g, b);
    }
}
