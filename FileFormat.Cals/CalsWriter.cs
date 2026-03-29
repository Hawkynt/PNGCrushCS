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
    var bytesPerRow = (file.Width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * file.Height;
    var fileSize = CalsHeaderParser.HeaderSize + expectedPixelBytes;

    var result = new byte[fileSize];
    header.AsSpan(0, CalsHeaderParser.HeaderSize).CopyTo(result.AsSpan(0));

    var copyLen = Math.Min(expectedPixelBytes, file.PixelData.Length);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(CalsHeaderParser.HeaderSize));

    return result;
  }
}
