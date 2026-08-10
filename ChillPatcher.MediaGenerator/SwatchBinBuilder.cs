using System;
using System.IO;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using SkiaSharp;

namespace ChillPatcher.MediaGenerator;

/// <summary>
/// Parses and generates FH6 SwatchBin texture files.
/// Format: burG container → BCXT chunk → [HCXT + mip table] → BC7 pixel data.
/// </summary>
static class SwatchBinBuilder
{
    // ─── Header parsing ───

    public readonly struct SwatchBinInfo
    {
        public readonly int HeaderSize;       // burG[0x08] — offset where pixel data starts
        public readonly int TotalSize;         // burG[0x0C] — total file size
        public readonly int DataSize;          // size of pixel data region
        public readonly int Width;             // decoded from HCXT
        public readonly int Height;            // decoded from HCXT
        public readonly CompressionFormat Format;
        public readonly byte[] HeaderBytes;    // everything before pixel data

        public SwatchBinInfo(int headerSize, int totalSize, int width, int height,
            CompressionFormat format, byte[] headerBytes)
        {
            HeaderSize = headerSize;
            TotalSize = totalSize;
            DataSize = totalSize - headerSize;
            Width = width;
            Height = height;
            Format = format;
            HeaderBytes = headerBytes;
        }
    }

    /// <summary>Parse a swatchbin to extract dimensions and header.</summary>
    public static SwatchBinInfo Parse(byte[] data)
    {
        if (data.Length < 0x60)
            throw new InvalidDataException("SwatchBin too small");

        // burG header
        var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "burG")
            throw new InvalidDataException($"Not a swatchbin: bad magic '{magic}'");

        int headerSize = BitConverter.ToInt32(data, 0x08);
        int totalSize = BitConverter.ToInt32(data, 0x0C);

        // BCXT header
        var bcxtMagic = System.Text.Encoding.ASCII.GetString(data, 0x14, 4);
        if (bcxtMagic != "BCXT")
            throw new InvalidDataException("Missing BCXT chunk");

        // HCXT header (at 0x2C)
        var hcxtMagic = System.Text.Encoding.ASCII.GetString(data, 0x2C, 4);
        if (hcxtMagic != "HCXT")
            throw new InvalidDataException("Missing HCXT chunk");

        // Width/Height: stored after HCXT sub-header.
        // For radio logos: at offset 0x4C (width) and 0x50 (height)
        int width = BitConverter.ToInt32(data, 0x4C);
        int height = BitConverter.ToInt32(data, 0x50);

        // Detect format from HCXT flags (byte at 0x5B: 0x06=BC7, 0x01=BC1)
        CompressionFormat format = data[0x5B] switch
        {
            0x06 => CompressionFormat.Bc7,
            0x01 => CompressionFormat.Bc1,
            _ => CompressionFormat.Bc7  // default for radio logos
        };

        var headerBytes = new byte[headerSize];
        Array.Copy(data, 0, headerBytes, 0, headerSize);

        return new SwatchBinInfo(headerSize, totalSize, width, height, format, headerBytes);
    }

    // ─── Build from PNG ───

    /// <summary>
    /// Generate a swatchbin from a PNG, matching the format of an existing target.
    /// PNG is resized to match target dimensions.
    /// </summary>
    public static byte[] BuildFromPng(string pngPath, SwatchBinInfo targetInfo)
    {
        if (!File.Exists(pngPath))
            throw new FileNotFoundException($"PNG not found: {pngPath}");

        // Load PNG with SkiaSharp
        using var skBitmap = SKBitmap.Decode(pngPath);
        if (skBitmap == null)
            throw new InvalidDataException($"Failed to decode PNG: {pngPath}");

        int tw = targetInfo.Width;
        int th = targetInfo.Height;
        int topMipSize = GetMipDataSize(tw, th, targetInfo.Format);
        bool generateMipMaps = targetInfo.DataSize > topMipSize;

        Console.WriteLine($"[swb] PNG loaded: {skBitmap.Width}x{skBitmap.Height} -> resizing to {tw}x{th}");
        Console.WriteLine($"[swb] Target data: {targetInfo.DataSize:N0} bytes, top mip: {topMipSize:N0} bytes, mipmaps={generateMipMaps}");

        // BCnEncoder expects straight (non-premultiplied) RGBA. Keeping the
        // source in premultiplied form makes transparent edges encode darker
        // than intended and can produce visibly corrupted purple/black logos.
        using var normalized = new SKBitmap(new SKImageInfo(skBitmap.Width, skBitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(skBitmap, 0, 0);
        }

        // Resize with SkiaSharp 3.x API (SKSamplingOptions instead of deprecated SKFilterQuality)
        using var resized = normalized.Resize(new SKSizeI(tw, th), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        if (resized == null)
            throw new InvalidOperationException("Resize failed");

        // Extract RGBA bytes
        var rgba = CopyRgbaPixels(resized, tw, th);

        Console.WriteLine($"[swb] Extracted {rgba.Length:N0} RGBA bytes, compressing to {targetInfo.Format}...");

        // Compress — BCnEncoder.Net API: EncodeToRawBytes(Span<byte>, width, height, PixelFormat) returns byte[][]
        var encoder = new BcEncoder
        {
            OutputOptions =
            {
                Quality = CompressionQuality.BestQuality,
                Format = targetInfo.Format,
                GenerateMipMaps = generateMipMaps
            }
        };

        var allMips = encoder.EncodeToRawBytes(rgba.AsSpan(), tw, th, PixelFormat.Rgba32);

        // Flatten all mip levels into single array
        long totalLen = 0;
        foreach (var mip in allMips)
            totalLen += mip.Length;

        var compressed = new byte[totalLen];
        long offset = 0;
        foreach (var mip in allMips)
        {
            Array.Copy(mip, 0, compressed, offset, mip.Length);
            offset += mip.Length;
        }

        Console.WriteLine($"[swb] {targetInfo.Format}: {allMips.Length} mip level(s), {totalLen:N0} bytes total");
        if (compressed.Length != targetInfo.DataSize)
        {
            throw new InvalidDataException(
                $"Compressed data size mismatch: generated {compressed.Length:N0} bytes, target expects {targetInfo.DataSize:N0} bytes.");
        }

        // Build swatchbin: header + compressed data
        int newTotalSize = targetInfo.HeaderSize + compressed.Length;
        var result = new byte[newTotalSize];

        // Copy header
        Array.Copy(targetInfo.HeaderBytes, 0, result, 0, targetInfo.HeaderSize);

        // Update only fields that describe the replacement payload. Other
        // container metadata (including the texture descriptor at 0x84) is
        // format-specific and must remain exactly as in the target template.
        WriteU32(result, 0x0C, (uint)newTotalSize);              // burG total size
        WriteU32(result, 0x24, (uint)compressed.Length);          // BCXT dataSize
        WriteU32(result, 0x28, (uint)compressed.Length);          // BCXT dataSizeDup

        // Copy compressed pixel data
        Array.Copy(compressed, 0, result, targetInfo.HeaderSize, compressed.Length);

        Console.WriteLine($"[swb] Built swatchbin: {newTotalSize:N0} bytes " +
                          $"({targetInfo.HeaderSize} header + {compressed.Length} BCn data)");

        return result;
    }

    static int GetMipDataSize(int width, int height, CompressionFormat format)
    {
        int blockBytes = format == CompressionFormat.Bc1 ? 8 : 16;
        int blocksWide = Math.Max(1, (width + 3) / 4);
        int blocksHigh = Math.Max(1, (height + 3) / 4);
        return blocksWide * blocksHigh * blockBytes;
    }

    static byte[] CopyRgbaPixels(SKBitmap bitmap, int width, int height)
    {
        int pixelBytes = width * 4;
        int expectedBytes = pixelBytes * height;
        var rgba = new byte[expectedBytes];
        var ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero)
            throw new InvalidOperationException("Failed to access resized pixels");

        int rowBytes = bitmap.RowBytes;
        if (rowBytes == pixelBytes)
        {
            System.Runtime.InteropServices.Marshal.Copy(ptr, rgba, 0, expectedBytes);
            return rgba;
        }

        var source = new byte[rowBytes * height];
        System.Runtime.InteropServices.Marshal.Copy(ptr, source, 0, source.Length);
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(source, row * rowBytes, rgba, row * pixelBytes, pixelBytes);
        }
        return rgba;
    }

    static void WriteU32(byte[] buf, int off, uint val) => BitConverter.GetBytes(val).CopyTo(buf, off);
}
