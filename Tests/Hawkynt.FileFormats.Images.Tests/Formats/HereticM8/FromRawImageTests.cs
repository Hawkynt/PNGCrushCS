using System;
using FileFormat.Core;
using FileFormat.HereticM8;

namespace FileFormat.HereticM8.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Picture(int width = 17, int height = 9) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i % 4 * 80);
      pixels[i * 3 + 1] = (byte)(i % 3 * 90);
      pixels[i * 3 + 2] = (byte)(i % 2 * 200);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheSizeAndTheIndicesComeBack() {
    var original = HereticM8File.FromRawImage(_Picture());

    var restored = HereticM8Reader.FromBytes(HereticM8Writer.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(17));
      Assert.That(restored.Height, Is.EqualTo(9));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OnlyLevelZeroIsStated() {
    // The other fifteen levels stay at nought: that says the file holds no smaller copies, rather
    // than that it holds empty ones at offset zero.
    var bytes = HereticM8Writer.ToBytes(HereticM8File.FromRawImage(_Picture()));
    var offsets = HereticM8File.WidthsOffset + HereticM8File.Levels * 8;

    Assert.Multiple(() => {
      Assert.That(BitConverter.ToInt32(bytes, 0), Is.EqualTo(HereticM8File.Version));
      Assert.That(BitConverter.ToInt32(bytes, HereticM8File.WidthsOffset), Is.EqualTo(17));
      Assert.That(BitConverter.ToInt32(bytes, HereticM8File.WidthsOffset + 4), Is.EqualTo(0), "level one is not stated");
      Assert.That(BitConverter.ToInt32(bytes, offsets + 4), Is.EqualTo(0));
    });
  }
}
