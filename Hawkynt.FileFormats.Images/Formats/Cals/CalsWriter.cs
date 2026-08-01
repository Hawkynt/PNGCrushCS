using System;
using FileFormat.Ccitt;

namespace FileFormat.Cals;

/// <summary>Assembles CALS raster file bytes from a <see cref="CalsFile"/>.</summary>
public static class CalsWriter {

  public static byte[] ToBytes(CalsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(CalsFile file) {
    var header = CalsHeaderParser.Format(file);
    var bytesPerRow = (file.Width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * file.Height;

    // A type 1 raster is Group 4 by definition, so the pixels are compressed on the way out. The
    // uncompressed bits were being written straight through, which produced files no CALS reader
    // could make sense of — and that this one could only read back because it made the same mistake.
    var pixels = new byte[expectedPixelBytes];
    file.PixelData.AsSpan(0, Math.Min(expectedPixelBytes, file.PixelData.Length)).CopyTo(pixels);
    var compressed = CcittG4Encoder.Encode(pixels, file.Width, file.Height);

    var result = new byte[CalsHeaderParser.HeaderSize + compressed.Length];
    header.AsSpan(0, CalsHeaderParser.HeaderSize).CopyTo(result.AsSpan(0));
    compressed.CopyTo(result.AsSpan(CalsHeaderParser.HeaderSize));

    return result;
  }
}
