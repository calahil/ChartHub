using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ChartHub.Conversion.Image;

/// <summary>
/// Decodes a Rock Band <c>.png_xbox</c> texture (DXT1 or DXT5 compressed) to a PNG byte array.
/// </summary>
/// <remarks>
/// Format layout (all values little-endian unless noted):
/// <list type="table">
///   <item><term>[0x00]</term><description>Format identifier: 0x08 = DXT1, 0x18 = DXT5</description></item>
///   <item><term>[0x01]</term><description>[reserved]</description></item>
///   <item><term>[0x02–0x03]</term><description>Width (LE16)</description></item>
///   <item><term>[0x04–0x05]</term><description>Height (LE16)</description></item>
///   <item><term>[0x06–0x07]</term><description>Mip-map count (LE16)</description></item>
///   <item><term>[0x08–0x1F]</term><description>[reserved / padding]</description></item>
///   <item><term>[0x20–]</term><description>DXT-compressed pixel data (tiled, then de-tiled)</description></item>
/// </list>
/// The pixel data is stored in Xbox 360 tile order (4x4-block row swizzle).  This decoder
/// converts tile order to linear order before colour decoding.
/// </remarks>
internal static class PngXboxDecoder
{
    private const int HeaderSize = 0x20;
    private const int LegacyDxt5HeaderSize = 0x20;
    private const byte FormatDxt1 = 0x08;
    private const byte FormatDxt5 = 0x18;
    private const byte FormatLegacyVersion = 0x01;

    /// <summary>Decodes a <c>.png_xbox</c> byte array and returns PNG-encoded bytes.</summary>
    public static byte[] Decode(byte[] rawBytes)
    {
        if (rawBytes.Length < HeaderSize)
        {
            throw new InvalidDataException("PNG Xbox texture header is too short.");
        }

        byte fmt = rawBytes[0];
        int width = rawBytes[2] | (rawBytes[3] << 8);
        int height = rawBytes[4] | (rawBytes[5] << 8);

        bool isDxt5 = fmt == FormatDxt5;
        if (fmt == FormatLegacyVersion)
        {
            return DecodeLegacyDxt5(rawBytes);
        }

        if (fmt != FormatDxt1 && fmt != FormatDxt5)
        {
            throw new InvalidDataException($"Unsupported PNG Xbox format identifier: 0x{fmt:X2}. Expected DXT1 (0x08) or DXT5 (0x18).");
        }

        int blockW = (width + 3) / 4;
        int blockH = (height + 3) / 4;
        int bytesPerBlock = isDxt5 ? 16 : 8;
        int dxtDataLen = blockW * blockH * bytesPerBlock;

        byte[] dxtData = rawBytes[HeaderSize..(HeaderSize + dxtDataLen)];

        // Xbox 360 tile de-swizzle
        byte[] linearDxt = Detile(dxtData, blockW, blockH, bytesPerBlock);

        // Decode DXT to RGBA
        byte[] rgba = isDxt5
            ? DecodeDxt5(linearDxt, blockW, blockH)
            : DecodeDxt1(linearDxt, blockW, blockH);

        // Encode as PNG via ImageSharp
        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static byte[] DecodeLegacyDxt5(byte[] rawBytes)
    {
        const int width = 256;
        const int height = 256;
        const int bytesPerBlock = 16;

        int blockW = (width + 3) / 4;
        int blockH = (height + 3) / 4;
        int dxtDataLen = blockW * blockH * bytesPerBlock;

        if (rawBytes.Length < LegacyDxt5HeaderSize + dxtDataLen)
        {
            throw new InvalidDataException("Legacy PNG Xbox texture is too short for a 256x256 DXT5 top mip.");
        }

        byte[] dxtData = rawBytes[LegacyDxt5HeaderSize..(LegacyDxt5HeaderSize + dxtDataLen)];
        Swap16InPlace(dxtData);

        byte[] linearDxt = Detile(dxtData, blockW, blockH, bytesPerBlock);
        byte[] rgba = DecodeDxt5(linearDxt, blockW, blockH);

        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static void Swap16InPlace(byte[] data)
    {
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            (data[i], data[i + 1]) = (data[i + 1], data[i]);
        }
    }

    // ---- Xbox 360 tile order de-swizzle ------------------------------------

    /// <summary>
    /// Converts Xbox 360 tiled DXT block layout to linear (row-major) layout.
    /// Xbox 360 stores blocks in a 2D Morton-curve (Z-order) swizzle.
    /// </summary>
    private static byte[] Detile(byte[] src, int blockW, int blockH, int blockBytes)
    {
        byte[] dst = new byte[src.Length];

        // Tile size in blocks (Xbox 360 uses 8×8-block tiles for DXT textures)
        const int tileBlockSize = 4; // 4 blocks × 4 blocks = one tile unit
        int tilesX = Math.Max(1, (blockW + tileBlockSize - 1) / tileBlockSize);
        int tilesY = Math.Max(1, (blockH + tileBlockSize - 1) / tileBlockSize);

        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                for (int by = 0; by < tileBlockSize; by++)
                {
                    for (int bx = 0; bx < tileBlockSize; bx++)
                    {
                        int srcBlockX = tx * tileBlockSize + MortonX(bx, by);
                        int srcBlockY = ty * tileBlockSize + MortonY(bx, by);
                        int dstBlockX = tx * tileBlockSize + bx;
                        int dstBlockY = ty * tileBlockSize + by;

                        if (srcBlockX >= blockW || srcBlockY >= blockH)
                        {
                            continue;
                        }

                        if (dstBlockX >= blockW || dstBlockY >= blockH)
                        {
                            continue;
                        }

                        int srcOff = (srcBlockY * blockW + srcBlockX) * blockBytes;
                        int dstOff = (dstBlockY * blockW + dstBlockX) * blockBytes;

                        Array.Copy(src, srcOff, dst, dstOff, blockBytes);
                    }
                }
            }
        }

        return dst;
    }

    private static int MortonX(int x, int y) => Deinterleave(InterleaveZOrder(x, y));
    private static int MortonY(int x, int y) => Deinterleave(InterleaveZOrder(x, y) >> 1);

    private static int InterleaveZOrder(int x, int y)
    {
        // Spread bits: interleave x into even bits, y into odd bits
        x = SpreadBits(x);
        y = SpreadBits(y);
        return x | (y << 1);
    }

    private static int SpreadBits(int v)
    {
        v = (v | (v << 8)) & 0x00FF00FF;
        v = (v | (v << 4)) & 0x0F0F0F0F;
        v = (v | (v << 2)) & 0x33333333;
        v = (v | (v << 1)) & 0x55555555;
        return v;
    }

    private static int Deinterleave(int v)
    {
        v &= 0x55555555;
        v = (v | (v >> 1)) & 0x33333333;
        v = (v | (v >> 2)) & 0x0F0F0F0F;
        v = (v | (v >> 4)) & 0x00FF00FF;
        v = (v | (v >> 8)) & 0x0000FFFF;
        return v;
    }

    // ---- DXT1 decoder -------------------------------------------------------

    private static byte[] DecodeDxt1(byte[] dxt, int blockW, int blockH)
    {
        int width = blockW * 4;
        int height = blockH * 4;
        byte[] pixels = new byte[width * height * 4];

        for (int by = 0; by < blockH; by++)
        {
            for (int bx = 0; bx < blockW; bx++)
            {
                int srcOff = (by * blockW + bx) * 8;
                ushort c0Raw = (ushort)(dxt[srcOff] | (dxt[srcOff + 1] << 8));
                ushort c1Raw = (ushort)(dxt[srcOff + 2] | (dxt[srcOff + 3] << 8));
                uint lookup = (uint)(dxt[srcOff + 4] | (dxt[srcOff + 5] << 8) | (dxt[srcOff + 6] << 16) | (dxt[srcOff + 7] << 24));

                (byte r0, byte g0, byte b0) = Rgb565ToRgb(c0Raw);
                (byte r1, byte g1, byte b1) = Rgb565ToRgb(c1Raw);

                (byte, byte, byte, byte)[] colours =
                [
                    (r0, g0, b0, (byte)0xFF),
                    (r1, g1, b1, (byte)0xFF),
                    c0Raw > c1Raw
                        ? ((byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3), (byte)0xFF)
                        : ((byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2), (byte)0xFF),
                    c0Raw > c1Raw
                        ? ((byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3), (byte)0xFF)
                        : ((byte)0, (byte)0, (byte)0, (byte)0),
                ];

                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        int idx = (int)((lookup >> ((py * 4 + px) * 2)) & 0x03);
                        int dstPixel = ((by * 4 + py) * width + (bx * 4 + px)) * 4;
                        (pixels[dstPixel], pixels[dstPixel + 1], pixels[dstPixel + 2], pixels[dstPixel + 3]) = colours[idx];
                    }
                }
            }
        }

        return pixels;
    }

    // ---- DXT5 decoder -------------------------------------------------------

    private static byte[] DecodeDxt5(byte[] dxt, int blockW, int blockH)
    {
        int width = blockW * 4;
        int height = blockH * 4;
        byte[] pixels = new byte[width * height * 4];

        for (int by = 0; by < blockH; by++)
        {
            for (int bx = 0; bx < blockW; bx++)
            {
                int srcOff = (by * blockW + bx) * 16;

                // Alpha block (8 bytes)
                byte a0 = dxt[srcOff];
                byte a1 = dxt[srcOff + 1];
                ulong alphaLookup = 0;
                for (int k = 0; k < 6; k++)
                {
                    alphaLookup |= (ulong)dxt[srcOff + 2 + k] << (k * 8);
                }

                byte[] alphas = new byte[8];
                alphas[0] = a0;
                alphas[1] = a1;
                if (a0 > a1)
                {
                    alphas[2] = (byte)((6 * a0 + 1 * a1) / 7);
                    alphas[3] = (byte)((5 * a0 + 2 * a1) / 7);
                    alphas[4] = (byte)((4 * a0 + 3 * a1) / 7);
                    alphas[5] = (byte)((3 * a0 + 4 * a1) / 7);
                    alphas[6] = (byte)((2 * a0 + 5 * a1) / 7);
                    alphas[7] = (byte)((1 * a0 + 6 * a1) / 7);
                }
                else
                {
                    alphas[2] = (byte)((4 * a0 + 1 * a1) / 5);
                    alphas[3] = (byte)((3 * a0 + 2 * a1) / 5);
                    alphas[4] = (byte)((2 * a0 + 3 * a1) / 5);
                    alphas[5] = (byte)((1 * a0 + 4 * a1) / 5);
                    alphas[6] = 0;
                    alphas[7] = 255;
                }

                // Colour block (8 bytes at offset +8 within the 16-byte block)
                int colourOff = srcOff + 8;
                ushort c0Raw = (ushort)(dxt[colourOff] | (dxt[colourOff + 1] << 8));
                ushort c1Raw = (ushort)(dxt[colourOff + 2] | (dxt[colourOff + 3] << 8));
                uint colLookup = (uint)(dxt[colourOff + 4] | (dxt[colourOff + 5] << 8) | (dxt[colourOff + 6] << 16) | (dxt[colourOff + 7] << 24));

                (byte r0, byte g0, byte b0) = Rgb565ToRgb(c0Raw);
                (byte r1, byte g1, byte b1) = Rgb565ToRgb(c1Raw);

                (byte, byte, byte)[] colours =
                [
                    (r0, g0, b0),
                    (r1, g1, b1),
                    ((byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3)),
                    ((byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3)),
                ];

                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        int pixIdx = py * 4 + px;
                        int aIdx = (int)((alphaLookup >> (pixIdx * 3)) & 0x07);
                        int cIdx = (int)((colLookup >> (pixIdx * 2)) & 0x03);

                        int dstPixel = ((by * 4 + py) * width + (bx * 4 + px)) * 4;
                        (pixels[dstPixel], pixels[dstPixel + 1], pixels[dstPixel + 2]) = colours[cIdx];
                        pixels[dstPixel + 3] = alphas[aIdx];
                    }
                }
            }
        }

        return pixels;
    }

    private static (byte r, byte g, byte b) Rgb565ToRgb(ushort v)
    {
        byte r = (byte)(((v >> 11) & 0x1F) * 255 / 31);
        byte g = (byte)(((v >> 5) & 0x3F) * 255 / 63);
        byte b = (byte)((v & 0x1F) * 255 / 31);
        return (r, g, b);
    }
}
