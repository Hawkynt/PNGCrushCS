using System;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Uncompressed4Plane() {
    var original = _CreateTestFile(16, 8, 4, IlbmCompression.None);

    var bytes = IlbmWriter.ToBytes(original);
    var restored = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
      Assert.That(restored.Compression, Is.EqualTo(IlbmCompression.None));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ByteRun1Compressed() {
    var original = _CreateTestFile(16, 8, 4, IlbmCompression.ByteRun1);

    var bytes = IlbmWriter.ToBytes(original);
    var restored = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
      Assert.That(restored.Compression, Is.EqualTo(IlbmCompression.ByteRun1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette() {
    var numColors = 16;
    var palette = new byte[numColors * 3];
    for (var i = 0; i < numColors; ++i) {
      palette[i * 3] = (byte)(i * 16);
      palette[i * 3 + 1] = (byte)(255 - i * 16);
      palette[i * 3 + 2] = (byte)(i * 8);
    }

    var pixelData = new byte[8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % numColors);

    var original = new IlbmFile {
      Width = 8,
      Height = 4,
      NumPlanes = 4,
      Compression = IlbmCompression.None,
      PixelData = pixelData,
      Palette = palette,
      XAspect = 10,
      YAspect = 11,
      PageWidth = 320,
      PageHeight = 200
    };

    var bytes = IlbmWriter.ToBytes(original);
    var restored = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.Not.Null);
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.XAspect, Is.EqualTo(10));
      Assert.That(restored.YAspect, Is.EqualTo(11));
      Assert.That(restored.PageWidth, Is.EqualTo(320));
      Assert.That(restored.PageHeight, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_1PlaneMonochrome() {
    var pixelData = new byte[16 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 2);

    var palette = new byte[2 * 3];
    palette[0] = 0; palette[1] = 0; palette[2] = 0;       // black
    palette[3] = 255; palette[4] = 255; palette[5] = 255; // white

    var original = new IlbmFile {
      Width = 16,
      Height = 2,
      NumPlanes = 1,
      Compression = IlbmCompression.None,
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = IlbmWriter.ToBytes(original);
    var restored = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(16));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.NumPlanes, Is.EqualTo(1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  private static IlbmFile _CreateTestFile(int width, int height, int numPlanes, IlbmCompression compression) {
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

    return new IlbmFile {
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      Compression = compression,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = 10,
      YAspect = 11,
      PageWidth = width,
      PageHeight = height,
      PixelData = pixelData,
      Palette = palette
    };
  }
}
