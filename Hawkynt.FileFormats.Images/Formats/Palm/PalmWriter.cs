using System;
using System.IO;

namespace FileFormat.Palm;

/// <summary>Assembles Palm OS Bitmap file bytes from pixel data.</summary>
public static class PalmWriter {

  public static byte[] ToBytes(PalmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(
      file.PixelData,
      file.Width,
      file.Height,
      file.BitsPerPixel,
      file.Compression,
      file.TransparentIndex,
      file.Palette
    );
  }

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int bitsPerPixel,
    PalmCompression compression,
    byte transparentIndex = 0,
    byte[]? palette = null
  ) {
    using var ms = new MemoryStream();

    var bytesPerRow = (ushort)((width * bitsPerPixel + 7) / 8);
    // Pad bytesPerRow to 2-byte boundary (Palm spec: word-aligned)
    if ((bytesPerRow & 1) != 0)
      ++bytesPerRow;

    var hasPalette = palette != null && palette.Length > 0;
    var hasTransparency = transparentIndex != 0 || (hasPalette && bitsPerPixel <= 8);

    ushort flags = 0;
    if (compression != PalmCompression.None)
      flags |= PalmHeader.FlagCompressed;
    if (hasPalette)
      flags |= PalmHeader.FlagHasColorTable;
    if (hasTransparency)
      flags |= PalmHeader.FlagHasTransparency;

    var header = new PalmHeader(
      Width: (ushort)width,
      Height: (ushort)height,
      BytesPerRow: bytesPerRow,
      Flags: flags,
      BitsPerPixel: (byte)bitsPerPixel,
      Version: 1,
      NextDepthOffset: 0,
      TransparentIndex: transparentIndex,
      CompressionType: (byte)compression,
      Reserved: 0
    );

    var headerBytes = new byte[PalmHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Write optional color table
    if (hasPalette) {
      var numEntries = palette!.Length / 3;
      ms.WriteByte((byte)(numEntries >> 8));
      ms.WriteByte((byte)(numEntries & 0xFF));

      for (var i = 0; i < numEntries; ++i) {
        ms.WriteByte((byte)i); // index
        ms.WriteByte(palette[i * 3]);     // R
        ms.WriteByte(palette[i * 3 + 1]); // G
        ms.WriteByte(palette[i * 3 + 2]); // B
      }
    }

    // Rows go out padded to the whole word the header promises, whatever stride they arrived in.
    var padded = _WithRowPadding(pixelData, ((width * bitsPerPixel) + 7) / 8, bytesPerRow, height);

    if (compression == PalmCompression.Rle) {
      var compressed = PalmRleCompressor.Compress(padded, bytesPerRow, height);
      ms.Write(compressed);
    } else
      ms.Write(padded, 0, Math.Min(padded.Length, bytesPerRow * height));

    return ms.ToArray();
  }

  /// <summary>Puts each row back on the whole-word boundary a Palm bitmap keeps them on.</summary>
  private static byte[] _WithRowPadding(byte[] pixelData, int usedRow, int storedRow, int height) {
    if (storedRow <= usedRow)
      return pixelData;

    var result = new byte[storedRow * height];
    for (var y = 0; y < height; ++y) {
      var from = y * usedRow;
      if (from + usedRow > pixelData.Length)
        break;

      pixelData.AsSpan(from, usedRow).CopyTo(result.AsSpan(y * storedRow));
    }

    return result;
  }
}
