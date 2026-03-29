using System;

namespace FileFormat.Fpx;

/// <summary>Assembles FPX file bytes from pixel data.</summary>
public static class FpxWriter {

  public static byte[] ToBytes(FpxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 3;
    var result = new byte[FpxHeader.StructSize + expectedPixelBytes];
    var span = result.AsSpan();

    // Write header fields via generated serializer (Version at 4, Width at 8, Height at 12)
    // WriteTo clears the span first when gaps exist, so magic must be written after
    var header = new FpxHeader(1, (uint)width, (uint)height);
    header.WriteTo(span);

    // Write magic bytes after WriteTo (which clears the span for gap-filling)
    result[0] = FpxHeader.Magic[0];
    result[1] = FpxHeader.Magic[1];
    result[2] = FpxHeader.Magic[2];
    result[3] = FpxHeader.Magic[3];

    // Write pixel data
    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(FpxHeader.StructSize));

    return result;
  }
}
