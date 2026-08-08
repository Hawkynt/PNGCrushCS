using System;
using FileFormat.Core;

namespace FileFormat.Ps2Txc.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 3);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesExactly() {
    var source = _Gradient(64, 32);
    var file = Ps2TxcFile.FromRawImage(source);
    var decoded = Ps2TxcFile.ToRawImage(Ps2TxcReader.FromBytes(Ps2TxcWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(file.BitsPerPixel, Is.EqualTo(24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TakesASizeThatIsNoPowerOfTwo() {
    // Textures are conventionally powers of two, but the header holds whatever size it is told.
    var decoded = Ps2TxcFile.ToRawImage(
      Ps2TxcReader.FromBytes(Ps2TxcWriter.ToBytes(Ps2TxcFile.FromRawImage(_Gradient(37, 11)))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
  }
}
