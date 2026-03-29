using System;
using System.Buffers.Binary;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

/// <summary>Shared test data builders for JPEG XL tests.</summary>
internal static class TestHelper {

  /// <summary>Builds a minimal JPEG XL ISOBMFF container with the given dimensions and pixel data.</summary>
  public static byte[] BuildMinimalJxlContainer(int width, int height, int componentCount, byte[]? pixelData = null) {
    pixelData ??= new byte[width * height * componentCount];

    var file = new JpegXlFile {
      Width = width,
      Height = height,
      ComponentCount = componentCount,
      PixelData = pixelData
    };

    return JpegXlWriter.ToBytes(file);
  }

  /// <summary>Builds a bare JPEG XL codestream (FF 0A + SizeHeader + component + pixels).</summary>
  public static byte[] BuildBareCodestream(int width, int height, int componentCount, byte[]? pixelData = null) {
    pixelData ??= new byte[width * height * componentCount];

    var sizeHeader = JpegXlSizeHeader.Encode(width, height);
    var result = new byte[2 + sizeHeader.Length + 1 + pixelData.Length];
    result[0] = 0xFF;
    result[1] = 0x0A;
    Array.Copy(sizeHeader, 0, result, 2, sizeHeader.Length);
    result[2 + sizeHeader.Length] = (byte)componentCount;
    if (pixelData.Length > 0)
      Array.Copy(pixelData, 0, result, 2 + sizeHeader.Length + 1, pixelData.Length);

    return result;
  }
}
