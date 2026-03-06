using System;
using FileFormat.Pcx;

namespace FileFormat.Pcx.Tests;

[TestFixture]
public sealed class PcxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb24_HasValidHeader() {
    var file = new PcxFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = new byte[4 * 4 * 3],
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var bytes = PcxWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x0A), "Manufacturer must be 0x0A");
    Assert.That(bytes[1], Is.EqualTo(5), "Version must be 5");
    Assert.That(bytes[2], Is.EqualTo(1), "Encoding must be 1 (RLE)");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Indexed8_HasVgaPalette() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i / 2);
    }

    var file = new PcxFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[] { 0, 1, 2, 3 },
      Palette = palette,
      PaletteColorCount = 256,
      ColorMode = PcxColorMode.Indexed8,
      PlaneConfig = PcxPlaneConfig.SinglePlane
    };

    var bytes = PcxWriter.ToBytes(file);

    var markerPos = bytes.Length - 769;
    Assert.That(markerPos, Is.GreaterThanOrEqualTo(128), "VGA palette marker must be after header");
    Assert.That(bytes[markerPos], Is.EqualTo(0x0C), "VGA palette must be preceded by 0x0C marker");
    Assert.That(bytes.Length - markerPos - 1, Is.EqualTo(768), "VGA palette must be exactly 768 bytes");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EvenBytesPerLine() {
    var file = new PcxFile {
      Width = 3, // Odd width to force padding
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[3 * 2 * 3],
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var bytes = PcxWriter.ToBytes(file);

    var bytesPerLine = BitConverter.ToInt16(bytes, 66);
    Assert.That(bytesPerLine % 2, Is.EqualTo(0), "bytesPerLine must be even per PCX spec");
    Assert.That(bytesPerLine, Is.GreaterThanOrEqualTo(file.Width));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderDimensions_Correct() {
    var file = new PcxFile {
      Width = 10,
      Height = 7,
      BitsPerPixel = 8,
      PixelData = new byte[10 * 7 * 3],
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var bytes = PcxWriter.ToBytes(file);

    var xMin = BitConverter.ToInt16(bytes, 4);
    var yMin = BitConverter.ToInt16(bytes, 6);
    var xMax = BitConverter.ToInt16(bytes, 8);
    var yMax = BitConverter.ToInt16(bytes, 10);

    Assert.That(xMin, Is.EqualTo(0));
    Assert.That(yMin, Is.EqualTo(0));
    Assert.That(xMax, Is.EqualTo(9), "xMax should be width - 1");
    Assert.That(yMax, Is.EqualTo(6), "yMax should be height - 1");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Indexed4_UsesEgaPalette() {
    var palette = new byte[16 * 3];
    palette[0] = 0x11; palette[1] = 0x22; palette[2] = 0x33;
    palette[3] = 0xAA; palette[4] = 0xBB; palette[5] = 0xCC;
    palette[6] = 0x44; palette[7] = 0x55; palette[8] = 0x66;
    palette[9] = 0x77; palette[10] = 0x88; palette[11] = 0x99;

    var file = new PcxFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 4,
      PixelData = new byte[] { 0x01, 0x23, 0x01, 0x23 }, // 2 bytes per row, 2 rows
      Palette = palette,
      PaletteColorCount = 16,
      ColorMode = PcxColorMode.Indexed4,
      PlaneConfig = PcxPlaneConfig.SinglePlane
    };

    var bytes = PcxWriter.ToBytes(file);

    Assert.That(bytes[16], Is.EqualTo(0x11), "EGA palette R[0] at offset 16");
    Assert.That(bytes[17], Is.EqualTo(0x22), "EGA palette G[0] at offset 17");
    Assert.That(bytes[18], Is.EqualTo(0x33), "EGA palette B[0] at offset 18");
    Assert.That(bytes[19], Is.EqualTo(0xAA), "EGA palette R[1] at offset 19");
    Assert.That(bytes[20], Is.EqualTo(0xBB), "EGA palette G[1] at offset 20");
    Assert.That(bytes[21], Is.EqualTo(0xCC), "EGA palette B[1] at offset 21");
  }
}
