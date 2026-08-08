using System;
using FileFormat.Core;

namespace FileFormat.Sprite64.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Stripes() {
    const int width = 24, height = 21;
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var on = (x + y) % 2 == 0;
      var o = (y * width + x) * 3;
      var value = on ? (byte)255 : (byte)0;
      data[o] = value;
      data[o + 1] = value;
      data[o + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Stripes_ReproducesExactly() {
    var source = _Stripes();
    var file = Sprite64File.FromRawImage(source);
    var restored = Sprite64Reader.FromBytes(Sprite64Writer.ToBytes(file));
    var decoded = Sprite64File.ToRawImage(restored);
    var decodedRgb = PixelConverter.Convert(decoded, PixelFormat.Rgb24);

    Assert.That(decodedRgb.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    // This sheet has one size and no other, so a picture of a different size is brought to it
    // rather than refused — which is what the rest of the library does and what a converter is for.
    static RawImage Raw(int width, int height)
      => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = new byte[width * height * 3] };

    var small = Sprite64File.ToRawImage(Sprite64File.FromRawImage(Raw(10, 10)));
    var large = Sprite64File.ToRawImage(Sprite64File.FromRawImage(Raw(640, 480)));

    Assert.That((small.Width, small.Height), Is.EqualTo((large.Width, large.Height)));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAMonoSprite()
    => Assert.That(Sprite64File.FromRawImage(_Stripes()).IsMulticolor, Is.False);
}
