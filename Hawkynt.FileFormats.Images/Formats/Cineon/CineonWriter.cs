using System;
using System.IO;

namespace FileFormat.Cineon;

/// <summary>Assembles Cineon file bytes from pixel data.</summary>
public static class CineonWriter {

  public static byte[] ToBytes(CineonFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.BitsPerSample,
    file.Orientation
  );

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int bitsPerSample,
    byte orientation
  ) {
    var dataOffset = CineonHeader.StructSize;
    var fileSize = dataOffset + pixelData.Length;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new CineonHeader(
      CineonHeader.MagicNumber,
      dataOffset,
      CineonHeader.StructSize,
      0,
      0,
      fileSize,
      "V4.5",
      string.Empty,
      string.Empty,
      string.Empty,
      orientation,
      1,
      0,
      0,
      (byte)bitsPerSample,
      width,
      height,
      0f,
      0f,
      1023f,
      2.046f
    );

    header.WriteTo(span);
    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(dataOffset));

    return result;
  }
}
