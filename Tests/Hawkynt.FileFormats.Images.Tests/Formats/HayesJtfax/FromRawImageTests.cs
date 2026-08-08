using System;
using FileFormat.Core;

namespace FileFormat.HayesJtfax.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A black-and-white checkerboard at a width that is not a multiple of eight, so the
  /// padding bits at the end of each row are exercised.</summary>
  private static RawImage _Checkerboard(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var value = (x + y) % 2 == 0 ? (byte)0 : (byte)255;
      var at = (y * width + x) * 3;
      data[at] = value;
      data[at + 1] = value;
      data[at + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Checkerboard_ReproducesExactly() {
    var source = _Checkerboard(13, 7);
    var file = HayesJtfaxFile.FromRawImage(source);
    var restored = HayesJtfaxReader.FromBytes(HayesJtfaxWriter.ToBytes(file));
    var decoded = HayesJtfaxFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((13, 7)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_PadsEachRowToAWholeNumberOfBytes() {
    var file = HayesJtfaxFile.FromRawImage(_Checkerboard(13, 7));

    Assert.That(file.PixelData, Has.Length.EqualTo(2 * 7));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsABitWhereTheInkIs() {
    // A fax is ink on paper: the decoder draws a set bit black, so dark pixels must set bits.
    var black = new RawImage {
      Width = 8, Height = 1, Format = PixelFormat.Rgb24, PixelData = new byte[24]
    };

    var file = HayesJtfaxFile.FromRawImage(black);

    Assert.That(file.PixelData[0], Is.EqualTo(0xFF));
  }
}
