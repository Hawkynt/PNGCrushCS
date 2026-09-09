using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Linq;
using FileFormat.JpegXl;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>Shared test data builders for JPEG XL tests.</summary>
internal static class TestHelper {

  /// <summary>The pattern's own size, and the stride the table is kept at.</summary>
  public const int DitherSize = 32;
  public const int DitherStride = 48;

  /// <summary>A file kept beside the tests.</summary>
  public static byte[] Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// libjxl's <c>kDither</c>, kept beside the fixtures rather than written out
  /// here because it is somebody else's table and reads as data.
  /// </summary>
  /// <remarks>
  /// Every eight-bit sample <c>djxl</c> writes has a value from this 32x32
  /// blue-noise pattern added to it first, offset differently per channel, so
  /// that quantisation shows as noise rather than as banding
  /// (<c>lib/jxl/render_pipeline/stage_write.cc</c>). Comparing an undithered
  /// decode against one of its <c>.ppm</c> files measures the pattern rather
  /// than the decoder.
  /// </remarks>
  public static float[] Dither() {
    var text = System.Text.Encoding.ASCII.GetString(Fixture("libjxl_dither.txt"));
    var values = text
      .Split('\n')
      .Where(line => !line.StartsWith('#'))
      .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      .Select(token => float.Parse(token, CultureInfo.InvariantCulture))
      .ToArray();

    Assert.That(values, Has.Length.EqualTo(DitherSize * DitherStride));
    return values;
  }

  /// <summary>What the pattern adds to the sample at (x, y) of channel c.</summary>
  public static float DitherAt(float[] dither, int x, int y, int channel) {
    var dx = (x + channel * 23) % DitherSize;
    var dy = (y + channel * 13) % DitherSize;
    return dither[dy * DitherStride + dx];
  }

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
