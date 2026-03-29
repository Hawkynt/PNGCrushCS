using System;
using System.IO;

namespace FileFormat.DuneGraph;

/// <summary>Assembles Atari Falcon DuneGraph file bytes from a DuneGraphFile.</summary>
public static class DuneGraphWriter {

  public static byte[] ToBytes(DuneGraphFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Convert RGB palette to Falcon format
    var falconPalette = new byte[DuneGraphFile.PaletteDataSize];
    DuneGraphFile.ConvertRgbPaletteToFalcon(
      file.Palette.AsSpan(0, Math.Min(file.Palette.Length, DuneGraphFile.PaletteEntryCount * 3)),
      falconPalette
    );

    byte[] pixelBytes;
    if (file.IsCompressed)
      pixelBytes = _CompressRle(file.PixelData);
    else {
      pixelBytes = new byte[DuneGraphFile.PixelDataSize];
      file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, DuneGraphFile.PixelDataSize)).CopyTo(pixelBytes);
    }

    var result = new byte[DuneGraphFile.PaletteDataSize + pixelBytes.Length];
    falconPalette.AsSpan().CopyTo(result);
    pixelBytes.AsSpan().CopyTo(result.AsSpan(DuneGraphFile.PaletteDataSize));
    return result;
  }

  /// <summary>Compresses pixel data using DuneGraph RLE: escape byte 0x00 + count + value for runs of 3+; non-zero literals pass through.</summary>
  internal static byte[] _CompressRle(byte[] data) {
    using var ms = new MemoryStream();
    var pos = 0;
    var len = Math.Min(data.Length, DuneGraphFile.PixelDataSize);

    while (pos < len) {
      var current = data[pos];
      var runLength = 1;
      while (pos + runLength < len && data[pos + runLength] == current && runLength < 255)
        ++runLength;

      if (current == DuneGraphFile.RleEscape) {
        // Zero bytes must always use RLE encoding to avoid ambiguity
        ms.WriteByte(DuneGraphFile.RleEscape);
        ms.WriteByte((byte)runLength);
        ms.WriteByte(current);
        pos += runLength;
      } else if (runLength >= 3) {
        ms.WriteByte(DuneGraphFile.RleEscape);
        ms.WriteByte((byte)runLength);
        ms.WriteByte(current);
        pos += runLength;
      } else {
        for (var i = 0; i < runLength; ++i)
          ms.WriteByte(current);
        pos += runLength;
      }
    }

    return ms.ToArray();
  }
}
