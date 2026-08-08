using System;
using FileFormat.Core;

namespace FileFormat.SonyMavica.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 17);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesExactly() {
    var source = _Gradient(64, 48);
    var decoded = SonyMavicaFile.ToRawImage(
      SonyMavicaReader.FromBytes(SonyMavicaWriter.ToBytes(SonyMavicaFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((64, 48)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var decoded = SonyMavicaFile.ToRawImage(
      SonyMavicaReader.FromBytes(SonyMavicaWriter.ToBytes(SonyMavicaFile.FromRawImage(_Gradient(37, 5)))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 5)));
  }
}
