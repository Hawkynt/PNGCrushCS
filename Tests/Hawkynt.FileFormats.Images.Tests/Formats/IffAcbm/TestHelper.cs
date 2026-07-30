using FileFormat.IffAcbm;

namespace FileFormat.IffAcbm.Tests;

/// <summary>Shared test data creation utilities.</summary>
internal static class TestHelper {

  /// <summary>Creates a test <see cref="IffAcbmFile"/> with deterministic pixel/palette data.</summary>
  internal static IffAcbmFile CreateTestFile(int width, int height, int numPlanes) {
    var numColors = 1 << numPlanes;
    var palette = new byte[numColors * 3];
    for (var i = 0; i < numColors; ++i) {
      palette[i * 3] = (byte)(i * 17 % 256);
      palette[i * 3 + 1] = (byte)(i * 31 % 256);
      palette[i * 3 + 2] = (byte)(i * 53 % 256);
    }

    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % numColors);

    return new IffAcbmFile {
      Width = width,
      Height = height,
      NumPlanes = (byte)numPlanes,
      PixelData = pixelData,
      Palette = palette,
      XAspect = 10,
      YAspect = 11,
      PageWidth = width,
      PageHeight = height,
    };
  }

  /// <summary>Builds a minimal ACBM byte array via writer for reader testing.</summary>
  internal static byte[] BuildMinimalAcbm(int width, int height, int numPlanes)
    => IffAcbmWriter.ToBytes(CreateTestFile(width, height, numPlanes));
}
