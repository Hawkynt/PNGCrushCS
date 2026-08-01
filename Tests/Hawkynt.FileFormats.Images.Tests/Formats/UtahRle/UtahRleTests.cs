using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.UtahRle.Tests;

/// <summary>
/// The Utah raster toolkit's format: a fifteen-byte header and two-byte instructions.
/// </summary>
/// <remarks>
/// Three things were wrong here at once, and each alone was enough to make the output unreadable:
/// the header was written a byte short, so every reader took the picture's first instruction as the
/// colour-map length; a fourth channel was declared without the flag that says the fourth is alpha,
/// describing a picture no reader has a shape for; and the instructions packed their command into
/// the top two bits, where the format puts it in the low six with bit six meaning the count did not
/// fit.
/// <para/>
/// Rows also run up the picture: the origin of one of these is its bottom left corner.
/// <para/>
/// Both halves are now byte-exact against ImageMagick on the same picture.
/// </remarks>
[TestFixture]
public sealed class UtahRleTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheFifteenthHeaderByte() {
    var bytes = UtahRleWriter.ToBytes(UtahRleFile.FromRawImage(_Picture(64, 48)));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0x52));
      Assert.That(bytes[1], Is.EqualTo(0xCC));
      Assert.That(bytes[6] | (bytes[7] << 8), Is.EqualTo(64));
      Assert.That(bytes[8] | (bytes[9] << 8), Is.EqualTo(48));
      Assert.That(bytes[11], Is.EqualTo(3), "three colour channels, the fourth being a flag not a channel");
      Assert.That(bytes[12], Is.EqualTo(8));
      Assert.That(bytes[13], Is.EqualTo(0), "no colour map");
      Assert.That(bytes[14], Is.EqualTo(0), "and so no length for one — this byte was missing");
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_PutsTheCommandInTheLowSixBits() {
    var bytes = UtahRleWriter.ToBytes(UtahRleFile.FromRawImage(_Picture(64, 48)));

    // The first instruction after the header selects a colour channel.
    Assert.That(bytes[15] & 0x3F, Is.EqualTo(2), "set colour");
    Assert.That(bytes[16], Is.EqualTo(0), "channel zero");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryPixel() {
    var original = _Picture(64, 48);
    var restored = UtahRleFile.ToRawImage(
      UtahRleReader.FromBytes(UtahRleWriter.ToBytes(UtahRleFile.FromRawImage(original))));

    Assert.That(restored.Width, Is.EqualTo(64));
    Assert.That(restored.Height, Is.EqualTo(48));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsARowsOrderTheRightWayUp() {
    // The top row and the bottom differ; a format stored bottom-first is easy to write upside down
    // and impossible to notice on a symmetric picture.
    var pixels = new byte[16 * 4 * 3];
    for (var x = 0; x < 16; ++x) {
      pixels[x * 3] = 255;
      pixels[(3 * 16 + x) * 3 + 2] = 255;
    }

    var original = new RawImage { Width = 16, Height = 4, Format = PixelFormat.Rgb24, PixelData = pixels };
    var restored = UtahRleFile.ToRawImage(
      UtahRleReader.FromBytes(UtahRleWriter.ToBytes(UtahRleFile.FromRawImage(original))));

    Assert.Multiple(() => {
      Assert.That(restored.PixelData[0], Is.EqualTo(255), "the top row is still red");
      Assert.That(restored.PixelData[(3 * 16) * 3 + 2], Is.EqualTo(255), "and the bottom still blue");
    });
  }
}
