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
      file.Palette,
      file.BytesPerRow
    );
  }

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int bitsPerPixel,
    PalmCompression compression,
    byte transparentIndex = 0,
    byte[]? palette = null,
    int bytesPerRowOverride = 0
  ) {
    using var ms = new MemoryStream();

    // The picture already carries its stride when it came from a file or from FromRawImage; only a
    // hand-built one needs it worked out, and Palm wants rows on a word boundary.
    var bytesPerRow = (ushort)(bytesPerRowOverride > 0 ? bytesPerRowOverride : PalmFile.RowStride(width, bitsPerPixel));

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

    // Write pixel data
    if (compression == PalmCompression.Rle) {
      var compressed = PalmRleCompressor.Compress(pixelData, bytesPerRow, height);
      ms.Write(compressed);
    } else
      ms.Write(pixelData, 0, Math.Min(pixelData.Length, bytesPerRow * height));

    return ms.ToArray();
  }
}
