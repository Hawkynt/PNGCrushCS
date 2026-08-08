using System;
using FileFormat.Core;

namespace FileFormat.ZxArtStudio.Tests;

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
    var file = ZxArtStudioFile.FromRawImage(source);
    var restored = ZxArtStudioReader.FromBytes(ZxArtStudioWriter.ToBytes(file));
    var decoded = ZxArtStudioFile.ToRawImage(restored);

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    // This screen has one size and no other, so a picture of a different size is brought to it
    // rather than refused — which is what the rest of the library does and what a converter is for.
    static RawImage Raw(int width, int height)
      => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = new byte[width * height * 3] };

    var small = ZxArtStudioFile.ToRawImage(ZxArtStudioFile.FromRawImage(Raw(100, 100)));
    var large = ZxArtStudioFile.ToRawImage(ZxArtStudioFile.FromRawImage(Raw(640, 480)));

    Assert.That((small.Width, small.Height), Is.EqualTo((large.Width, large.Height)));
  }
}
