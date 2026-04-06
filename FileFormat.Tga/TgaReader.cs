using System;
using System.IO;

namespace FileFormat.Tga;

/// <summary>Reads TGA files from bytes, streams, or file paths.</summary>
public static class TgaReader {

  public static TgaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TGA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TgaFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static TgaFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static TgaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < TgaHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid TGA file.");

    var span = data.AsSpan();
    var header = TgaHeader.ReadFrom(span);

    var idLength = header.IdLength;
    var colorMapType = header.ColorMapType;
    var imageType = header.ImageType;
    var colorMapFirstEntry = header.ColorMapFirstEntry;
    var colorMapLength = header.ColorMapLength;
    var colorMapEntrySize = header.ColorMapEntrySize;
    var width = header.Width;
    var height = header.Height;
    var bitsPerPixel = header.BitsPerPixel;
    var imageDescriptor = header.ImageDescriptor;

    var origin = (imageDescriptor & 0x20) != 0 ? TgaOrigin.TopLeft : TgaOrigin.BottomLeft;
    var alphaBits = imageDescriptor & 0x0F;

    // Skip header and image ID
    var offset = TgaHeader.StructSize + idLength;

    // Read color map
    byte[]? palette = null;
    var paletteColorCount = 0;
    if (colorMapType == 1 && colorMapLength > 0) {
      var entryBytes = (colorMapEntrySize + 7) / 8;
      paletteColorCount = colorMapLength;
      palette = new byte[paletteColorCount * 3];

      for (var i = 0; i < paletteColorCount; ++i) {
        if (entryBytes >= 3) {
          var b = span[offset];
          var g = span[offset + 1];
          var r = span[offset + 2];
          palette[i * 3] = r;
          palette[i * 3 + 1] = g;
          palette[i * 3 + 2] = b;
          offset += entryBytes;
        } else
          offset += entryBytes;
      }
    }

    // Determine compression and color mode
    var isRle = imageType is 9 or 10 or 11;
    var compression = isRle ? TgaCompression.Rle : TgaCompression.None;
    var baseType = isRle ? imageType - 8 : imageType;
    var colorMode = _DetectColorMode(baseType, bitsPerPixel, palette, paletteColorCount, alphaBits);

    // Read pixel data
    var bytesPerPixel = (bitsPerPixel + 7) / 8;
    var totalPixels = width * height;

    // Check for TGA 2.0 footer and exclude it from pixel data
    var pixelDataEnd = data.Length;
    if (data.Length >= TgaFooter.StructSize) {
      var footerSignature = "TRUEVISION-XFILE."u8;
      var footerStart = data.Length - TgaFooter.StructSize;
      var match = true;
      for (var i = 0; i < footerSignature.Length; ++i) {
        if (data[footerStart + 8 + i] != footerSignature[i]) {
          match = false;
          break;
        }
      }

      if (match)
        pixelDataEnd = footerStart;
    }

    var remainingBytes = pixelDataEnd - offset;
    var rawPixelData = span.Slice(offset, remainingBytes).ToArray();

    byte[] pixelData;
    if (isRle)
      pixelData = TgaRleCompressor.Decompress(rawPixelData, totalPixels, bytesPerPixel);
    else {
      pixelData = new byte[totalPixels * bytesPerPixel];
      rawPixelData.AsSpan(0, Math.Min(rawPixelData.Length, pixelData.Length)).CopyTo(pixelData.AsSpan(0));
    }

    // Reorder rows to top-down order if origin is bottom-left
    if (origin == TgaOrigin.BottomLeft) {
      var rowBytes = width * bytesPerPixel;
      var reordered = new byte[pixelData.Length];
      for (var row = 0; row < height; ++row) {
        var srcOffset = (height - 1 - row) * rowBytes;
        var dstOffset = row * rowBytes;
        pixelData.AsSpan(srcOffset, rowBytes).CopyTo(reordered.AsSpan(dstOffset));
      }

      pixelData = reordered;
    }

    return new TgaFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = paletteColorCount,
      Origin = origin,
      Compression = compression,
      ColorMode = colorMode
    };
  }

  private static TgaColorMode _DetectColorMode(int baseType, int bitsPerPixel, byte[]? palette, int paletteColorCount, int alphaBits) {
    // baseType: 1 = color-mapped, 2 = true-color, 3 = grayscale
    if (baseType == 3)
      return TgaColorMode.Grayscale8;

    if (baseType == 1 && palette != null && paletteColorCount > 0)
      return TgaColorMode.Indexed8;

    if (bitsPerPixel == 32)
      return TgaColorMode.Rgba32;

    if (bitsPerPixel == 24)
      return TgaColorMode.Rgb24;

    return TgaColorMode.Original;
  }
}
