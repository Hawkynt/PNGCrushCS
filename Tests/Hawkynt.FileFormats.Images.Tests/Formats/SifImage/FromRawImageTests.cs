using System;
using FileFormat.Core;

namespace FileFormat.SifImage.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 11);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesExactly() {
    var source = _Gradient(29, 13);
    var decoded = SifImageFile.ToRawImage(
      SifImageReader.FromBytes(SifImageWriter.ToBytes(SifImageFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((29, 13)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwnAndKeepsTheSize() {
    var indexed = PixelConverter.Convert(_Gradient(40, 9), PixelFormat.Indexed8);
    var file = SifImageFile.FromRawImage(indexed);

    Assert.Multiple(() => {
      Assert.That((file.Width, file.Height), Is.EqualTo((40, 9)));
      Assert.That(file.Bpp, Is.EqualTo(24));
      Assert.That(file.PixelData, Has.Length.EqualTo(40 * 9 * 3));
    });
  }
}
