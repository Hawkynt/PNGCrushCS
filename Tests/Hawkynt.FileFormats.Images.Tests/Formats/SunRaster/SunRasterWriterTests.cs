using System;
using System.Buffers.Binary;
using FileFormat.SunRaster;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class SunRasterWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb24_StartsWithMagic() {
    var file = new SunRasterFile {
      Width = 2,
      Height = 2,
      Depth = 24,
      PixelData = new byte[2 * 2 * 3],
      ColorMode = SunRasterColorMode.Rgb24
    };

    var bytes = SunRasterWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(SunRasterHeader.MagicValue));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Width_WrittenCorrectly() {
    var file = new SunRasterFile {
      Width = 100,
      Height = 50,
      Depth = 24,
      PixelData = new byte[100 * 50 * 3],
      ColorMode = SunRasterColorMode.Rgb24
    };

    var bytes = SunRasterWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(width, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Height_WrittenCorrectly() {
    var file = new SunRasterFile {
      Width = 100,
      Height = 50,
      Depth = 24,
      PixelData = new byte[100 * 50 * 3],
      ColorMode = SunRasterColorMode.Rgb24
    };

    var bytes = SunRasterWriter.ToBytes(file);

    var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8));
    Assert.That(height, Is.EqualTo(50));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Depth_WrittenCorrectly() {
    var file = new SunRasterFile {
      Width = 2,
      Height = 2,
      Depth = 8,
      PixelData = new byte[2 * 2],
      Palette = new byte[4 * 3],
      PaletteColorCount = 4,
      ColorMode = SunRasterColorMode.Palette8
    };

    var bytes = SunRasterWriter.ToBytes(file);

    var depth = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(12));
    Assert.That(depth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Palette_IncludesMapLength() {
    var palette = new byte[4 * 3];
    palette[0] = 255; // color 0: R=255

    var file = new SunRasterFile {
      Width = 2,
      Height = 2,
      Depth = 8,
      PixelData = new byte[2 * 2],
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = SunRasterColorMode.Palette8
    };

    var bytes = SunRasterWriter.ToBytes(file);

    var mapType = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(24));
    var mapLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(28));
    Assert.That(mapType, Is.EqualTo(1));
    Assert.That(mapLength, Is.EqualTo(4 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NoPalette_MapLengthIsZero() {
    var file = new SunRasterFile {
      Width = 2,
      Height = 2,
      Depth = 24,
      PixelData = new byte[2 * 2 * 3],
      ColorMode = SunRasterColorMode.Rgb24
    };

    var bytes = SunRasterWriter.ToBytes(file);

    var mapType = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(24));
    var mapLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(28));
    Assert.That(mapType, Is.EqualTo(0));
    Assert.That(mapLength, Is.EqualTo(0));
  }
}
