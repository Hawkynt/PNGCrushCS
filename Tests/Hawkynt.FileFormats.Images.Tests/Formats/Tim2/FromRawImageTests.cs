using System;
using FileFormat.Core;

namespace FileFormat.Tim2.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 19);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesExactly() {
    var source = _Gradient(64, 64);
    var decoded = Tim2File.ToRawImage(Tim2Reader.FromBytes(Tim2Writer.ToBytes(Tim2File.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((64, 64)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesOnePictureInTheTwentyFourBitMode() {
    // Thirty-two bits would carry alpha, but a PS2 texture's alpha runs 0 to 128 rather than 0 to
    // 255, so an opaque picture written that way reads back at half strength.
    var file = Tim2File.FromRawImage(_Gradient(8, 8));

    Assert.Multiple(() => {
      Assert.That(file.Pictures, Has.Count.EqualTo(1));
      Assert.That(file.Pictures[0].Format, Is.EqualTo(Tim2Format.Rgb24));
      Assert.That(file.Pictures[0].PaletteData, Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TakesASizeThatIsNoPowerOfTwo() {
    var decoded = Tim2File.ToRawImage(Tim2Reader.FromBytes(Tim2Writer.ToBytes(Tim2File.FromRawImage(_Gradient(23, 9)))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((23, 9)));
  }
}
