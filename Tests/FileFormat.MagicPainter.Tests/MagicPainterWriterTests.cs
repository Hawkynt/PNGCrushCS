using System;
using FileFormat.MagicPainter;

namespace FileFormat.MagicPainter.Tests;

[TestFixture]
public sealed class MagicPainterWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MagicPainterWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputSizeMatchesExpected() {
    var file = new MagicPainterFile {
      Width = 8,
      Height = 4,
      PaletteCount = 2,
      Palette = new byte[6],
      PixelData = new byte[32],
    };

    var bytes = MagicPainterWriter.ToBytes(file);

    // 6 header + 6 palette + 32 pixels = 44
    Assert.That(bytes.Length, Is.EqualTo(44));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthWrittenAsLE() {
    var file = new MagicPainterFile {
      Width = 0x0102,
      Height = 1,
      PaletteCount = 1,
      Palette = new byte[3],
      PixelData = new byte[0x0102],
    };

    var bytes = MagicPainterWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x02));
    Assert.That(bytes[1], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeightWrittenAsLE() {
    var file = new MagicPainterFile {
      Width = 1,
      Height = 0x0304,
      PaletteCount = 1,
      Palette = new byte[3],
      PixelData = new byte[0x0304],
    };

    var bytes = MagicPainterWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0x04));
    Assert.That(bytes[3], Is.EqualTo(0x03));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteCountWrittenAsLE() {
    var file = new MagicPainterFile {
      Width = 1,
      Height = 1,
      PaletteCount = 256,
      Palette = new byte[768],
      PixelData = new byte[1],
    };

    var bytes = MagicPainterWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0x00));
    Assert.That(bytes[5], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteDataWritten() {
    var palette = new byte[6];
    palette[0] = 0xFF; palette[1] = 0x00; palette[2] = 0x00;
    palette[3] = 0x00; palette[4] = 0xFF; palette[5] = 0x00;

    var file = new MagicPainterFile {
      Width = 1,
      Height = 1,
      PaletteCount = 2,
      Palette = palette,
      PixelData = new byte[1],
    };

    var bytes = MagicPainterWriter.ToBytes(file);

    Assert.That(bytes[6], Is.EqualTo(0xFF));
    Assert.That(bytes[7], Is.EqualTo(0x00));
    Assert.That(bytes[8], Is.EqualTo(0x00));
    Assert.That(bytes[9], Is.EqualTo(0x00));
    Assert.That(bytes[10], Is.EqualTo(0xFF));
    Assert.That(bytes[11], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataWrittenAfterPalette() {
    var pixels = new byte[4];
    pixels[0] = 0x01;
    pixels[1] = 0x00;
    pixels[2] = 0x01;
    pixels[3] = 0x00;

    var file = new MagicPainterFile {
      Width = 2,
      Height = 2,
      PaletteCount = 2,
      Palette = new byte[6],
      PixelData = pixels,
    };

    var bytes = MagicPainterWriter.ToBytes(file);

    var pixelOffset = 6 + 6; // header + palette
    Assert.That(bytes[pixelOffset], Is.EqualTo(0x01));
    Assert.That(bytes[pixelOffset + 1], Is.EqualTo(0x00));
    Assert.That(bytes[pixelOffset + 2], Is.EqualTo(0x01));
    Assert.That(bytes[pixelOffset + 3], Is.EqualTo(0x00));
  }
}
