using System;
using FileFormat.Core;

namespace FileFormat.Picasso64.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _SolidCells() {
    const int width = Picasso64File.FixedWidth, height = Picasso64File.FixedHeight;
    ReadOnlySpan<int> palette = [0, 1, 2, 3];
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / 8) * (width / 4) + x / 4;
      var color = Commodore64Graphics.HexColors[palette[cellIndex % 4]];
      var o = (y * width + x) * 4;
      data[o] = (byte)color;
      data[o + 1] = (byte)(color >> 8);
      data[o + 2] = (byte)(color >> 16);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SolidCells_ReproducesExactly() {
    var source = _SolidCells();
    var file = Picasso64File.FromRawImage(source);
    var restored = Picasso64Reader.FromBytes(Picasso64Writer.ToBytes(file));
    var decoded = Picasso64File.ToRawImage(restored);
    var decodedBgra = PixelConverter.Convert(decoded, PixelFormat.Bgra32);

    Assert.That(decodedBgra.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ScalesAPictureOfAnyOtherSize() {
    // This screen has one size and no other, so a picture of a different size is brought to it
    // rather than refused — which is what the rest of the library does and what a converter is for.
    static RawImage Raw(int width, int height)
      => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = new byte[width * height * 3] };

    var small = Picasso64File.ToRawImage(Picasso64File.FromRawImage(Raw(100, 100)));
    var large = Picasso64File.ToRawImage(Picasso64File.FromRawImage(Raw(640, 480)));

    Assert.That((small.Width, small.Height), Is.EqualTo((large.Width, large.Height)));
  }
}
