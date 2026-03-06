using System;
using System.Linq;
using System.Text;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class PngWriterTests {

  private static readonly byte[] _PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb_StartsWithPngSignature() {
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[3]]
    };

    var bytes = PngWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(8));
    Assert.That(bytes[..8], Is.EqualTo(_PngSignature));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Palette_IncludesPlteChunk() {
    var palette = new byte[12]; // 4 entries * 3 bytes
    palette[0] = 255;
    palette[6] = 255;

    var file = new PngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.Palette,
      Palette = palette,
      PaletteCount = 4,
      PixelData = [new byte[2], new byte[2]]
    };

    var bytes = PngWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("PLTE"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Transparency_IncludesTrnsChunk() {
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      Transparency = new byte[] { 0, 255, 0, 128, 0, 64 },
      PixelData = [new byte[3]]
    };

    var bytes = PngWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("tRNS"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EndsWithIend() {
    var file = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[3]]
    };

    var bytes = PngWriter.ToBytes(file);
    var last12 = bytes[^12..];

    Assert.That(last12[0..4], Is.EqualTo(new byte[] { 0, 0, 0, 0 }), "IEND length should be 0");

    var iendType = Encoding.ASCII.GetString(last12, 4, 4);
    Assert.That(iendType, Is.EqualTo("IEND"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasValidCrc32() {
    var file = new PngFile {
      Width = 4,
      Height = 4,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = Enumerable.Range(0, 4).Select(_ => new byte[12]).ToArray()
    };

    var bytes = PngWriter.ToBytes(file);

    Assert.DoesNotThrow(() => PngReader.FromBytes(bytes));
  }
}
