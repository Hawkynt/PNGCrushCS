using System;
using System.Text;
using FileFormat.Tga;

namespace FileFormat.Tga.Tests;

[TestFixture]
public sealed class TgaWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb24_Has18ByteHeader() {
    var file = new TgaFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 24,
      PixelData = new byte[4 * 4 * 3],
      ColorMode = TgaColorMode.Rgb24,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(18 + file.PixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgba32_SetsAlphaDescriptor() {
    var file = new TgaFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 32,
      PixelData = new byte[2 * 2 * 4],
      ColorMode = TgaColorMode.Rgba32,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(file);
    var imageDescriptor = bytes[17];

    Assert.That(imageDescriptor & 0x08, Is.EqualTo(0x08), "Alpha bits (8) should be set in imageDescriptor");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Indexed8_IncludesColorMap() {
    var palette = new byte[4 * 3]; // 4 colors, RGB
    for (var i = 0; i < 4; ++i) {
      palette[i * 3] = (byte)(i * 60);
      palette[i * 3 + 1] = (byte)(i * 40);
      palette[i * 3 + 2] = (byte)(i * 20);
    }

    var file = new TgaFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[] { 0, 1, 2, 3 },
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = TgaColorMode.Indexed8,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(file);

    Assert.That(bytes[1], Is.EqualTo(1), "colorMapType should be 1 for indexed images");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TopLeft_SetsOriginBit() {
    var file = new TgaFile {
      Width = 1,
      Height = 1,
      BitsPerPixel = 24,
      PixelData = new byte[3],
      ColorMode = TgaColorMode.Rgb24,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(file);
    var imageDescriptor = bytes[17];

    Assert.That(imageDescriptor & 0x20, Is.EqualTo(0x20), "Top-left origin bit should be set");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IncludesTga2Footer() {
    var file = new TgaFile {
      Width = 1,
      Height = 1,
      BitsPerPixel = 24,
      PixelData = new byte[3],
      ColorMode = TgaColorMode.Rgb24,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(file);
    var footer = Encoding.ASCII.GetString(bytes, bytes.Length - 18, 18);

    Assert.That(footer, Is.EqualTo("TRUEVISION-XFILE.\0"));
  }
}
