using System;

namespace FileFormat.BennetYeeFace;

/// <summary>Assembles Bennet Yee Face (.ybm) file bytes from pixel data.</summary>
public static class BennetYeeFaceWriter {

  public static byte[] ToBytes(BennetYeeFaceFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var stride = BennetYeeFaceFile.ComputeStride(width);
    var expectedPixelBytes = stride * height;
    var fileSize = BennetYeeFaceHeader.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new BennetYeeFaceHeader((ushort)width, (ushort)height);
    header.WriteTo(span);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(BennetYeeFaceHeader.StructSize));

    return result;
  }
}
