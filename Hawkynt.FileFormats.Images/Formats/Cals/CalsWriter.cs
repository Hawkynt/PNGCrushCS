using System;

namespace FileFormat.Cals;

/// <summary>Assembles CALS raster file bytes from a <see cref="CalsFile"/>.</summary>
public static class CalsWriter {

  public static byte[] ToBytes(CalsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(CalsFile file) {
    var header = CalsHeaderParser.Format(file);
    var compressed = FileFormat.Ccitt.CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);

    var result = new byte[CalsHeaderParser.HeaderSize + compressed.Length];
    header.AsSpan(0, CalsHeaderParser.HeaderSize).CopyTo(result.AsSpan(0));
    compressed.CopyTo(result.AsSpan(CalsHeaderParser.HeaderSize));

    return result;
  }
}
