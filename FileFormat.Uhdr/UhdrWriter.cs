using System;

namespace FileFormat.Uhdr;

/// <summary>Assembles UHDR file bytes from pixel data.</summary>
public static class UhdrWriter {

  public static byte[] ToBytes(UhdrFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 3;
    var result = new byte[UhdrHeader.StructSize + expectedPixelBytes];

    var header = new UhdrHeader(UhdrHeader.MagicValue, UhdrHeader.CurrentVersion, 0, (uint)width, (uint)height);
    header.WriteTo(result.AsSpan());

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(UhdrHeader.StructSize));

    return result;
  }
}
