using System;
using FileFormat.PcPaint;

namespace FileFormat.PcPaint.Tests;

[TestFixture]
public sealed class PcPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes_Are3412() {
    var file = _CreateMinimalFile(2, 2);
    var bytes = PcPaintWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x34));
    Assert.That(bytes[1], Is.EqualTo(0x12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_StoredCorrectly() {
    var file = _CreateMinimalFile(320, 200);
    var bytes = PcPaintWriter.ToBytes(file);

    var width = (ushort)(bytes[2] | (bytes[3] << 8));
    var height = (ushort)(bytes[4] | (bytes[5] << 8));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Offsets_StoredCorrectly() {
    var file = new PcPaintFile {
      Width = 2,
      Height = 2,
      XOffset = 10,
      YOffset = 20,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = new byte[4],
    };

    var bytes = PcPaintWriter.ToBytes(file);

    var xOffset = (ushort)(bytes[6] | (bytes[7] << 8));
    var yOffset = (ushort)(bytes[8] | (bytes[9] << 8));

    Assert.That(xOffset, Is.EqualTo(10));
    Assert.That(yOffset, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PlanesAndBpp_StoredCorrectly() {
    var file = _CreateMinimalFile(2, 2);
    var bytes = PcPaintWriter.ToBytes(file);

    Assert.That(bytes[10], Is.EqualTo(1));
    Assert.That(bytes[11], Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Aspect_StoredCorrectly() {
    var file = new PcPaintFile {
      Width = 2,
      Height = 2,
      Planes = 1,
      BitsPerPixel = 8,
      XAspect = 3,
      YAspect = 4,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = new byte[4],
    };

    var bytes = PcPaintWriter.ToBytes(file);

    var xAspect = (ushort)(bytes[12] | (bytes[13] << 8));
    var yAspect = (ushort)(bytes[14] | (bytes[15] << 8));

    Assert.That(xAspect, Is.EqualTo(3));
    Assert.That(yAspect, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteInfoLength_Is768() {
    var file = _CreateMinimalFile(2, 2);
    var bytes = PcPaintWriter.ToBytes(file);

    var paletteInfoLength = (ushort)(bytes[16] | (bytes[17] << 8));
    Assert.That(paletteInfoLength, Is.EqualTo(PcPaintFile.PaletteSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteData_IsPresent() {
    var palette = new byte[PcPaintFile.PaletteSize];
    palette[0] = 255;
    palette[1] = 128;
    palette[2] = 64;

    var file = new PcPaintFile {
      Width = 2,
      Height = 2,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = palette,
      PixelData = new byte[4],
    };

    var bytes = PcPaintWriter.ToBytes(file);

    Assert.That(bytes[PcPaintFile.HeaderSize], Is.EqualTo(255));
    Assert.That(bytes[PcPaintFile.HeaderSize + 1], Is.EqualTo(128));
    Assert.That(bytes[PcPaintFile.HeaderSize + 2], Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPalette_WritesZeroPaletteLength() {
    var file = new PcPaintFile {
      Width = 2,
      Height = 2,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = [],
      PixelData = new byte[4],
    };

    var bytes = PcPaintWriter.ToBytes(file);

    var paletteInfoLength = (ushort)(bytes[16] | (bytes[17] << 8));
    Assert.That(paletteInfoLength, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CompressRle_Empty_ReturnsEmpty() {
    var result = PcPaintWriter._CompressRle([]);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CompressRle_AllSame_ProducesSingleRun() {
    var data = new byte[] { 42, 42, 42, 42, 42 };
    var result = PcPaintWriter._CompressRle(data);

    Assert.That(result[0], Is.EqualTo(5));
    Assert.That(result[1], Is.EqualTo(42));
    Assert.That(result.Length, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void CompressRle_MixedData_RoundTrips() {
    var data = new byte[] { 1, 1, 1, 2, 2, 3 };
    var compressed = PcPaintWriter._CompressRle(data);
    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  private static PcPaintFile _CreateMinimalFile(int width, int height) => new() {
    Width = width,
    Height = height,
    Planes = 1,
    BitsPerPixel = 8,
    Palette = new byte[PcPaintFile.PaletteSize],
    PixelData = new byte[width * height],
  };
}
