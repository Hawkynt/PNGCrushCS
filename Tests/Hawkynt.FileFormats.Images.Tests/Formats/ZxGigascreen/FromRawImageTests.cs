using System;
using FileFormat.Core;

namespace FileFormat.ZxGigascreen.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Checkerboard() {
    const int width = 256, height = 192;
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellOn = (((x / 8) + (y / 8)) & 1) != 0;
      var o = (y * width + x) * 3;
      var value = cellOn ? (byte)255 : (byte)0;
      data[o] = value;
      data[o + 1] = value;
      data[o + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CheckerboardOfCells_ReproducesExactly() {
    var source = _Checkerboard();
    var file = ZxGigascreenFile.FromRawImage(source);
    var restored = ZxGigascreenReader.FromBytes(ZxGigascreenWriter.ToBytes(file));
    var decoded = ZxGigascreenFile.ToRawImage(restored);

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsWrongDimensions() {
    var raw = new RawImage { Width = 100, Height = 100, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 100 * 3] };

    Assert.Throws<ArgumentException>(() => ZxGigascreenFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_EncodesBothScreensIdentically() {
    var file = ZxGigascreenFile.FromRawImage(_Checkerboard());

    Assert.That(file.BitmapData2, Is.EqualTo(file.BitmapData1));
    Assert.That(file.AttributeData2, Is.EqualTo(file.AttributeData1));
  }
}
