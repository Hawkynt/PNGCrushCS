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
    // The pixels start after both halves of the header, not after the generic one: writing them at
    // 1024 laid them over the image descriptor, which is where a reader looks to find out how many
    // channels there are and how wide they run.
    var dataOffset = CineonHeader.ImageDataStart;
    var fileSize = dataOffset + pixelData.Length;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var maxData = (1 << bitsPerSample) - 1;
    const float maxDensity = 2.046f; // the printing density reference white stands at

    var header = new CineonHeader(
      CineonHeader.MagicNumber,
      dataOffset,
      CineonHeader.StructSize,
      CineonHeader.ImageDataStart - CineonHeader.StructSize,
      0,
      fileSize,
      "V4.5",
      string.Empty,
      string.Empty,
      string.Empty,
      orientation,
      3, // red, green and blue, each with its own element record below
      0,
      0,
      (byte)bitsPerSample,
      width,
      height,
      0f,
      0f,
      maxData,
      maxDensity,
      0,
      0,
      (byte)bitsPerSample,
      width,
      height,
      0f,
      0f,
      maxData,
      maxDensity,
      0,
      0,
      (byte)bitsPerSample,
      width,
      height,
      0f,
      0f,
      maxData,
      maxDensity
    );

    header.WriteTo(span);
    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(dataOffset));

    return result;
  }
}
