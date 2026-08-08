using System;
using FileFormat.Core;

namespace FileFormat.Flip64.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _SolidCells() {
    const int width = Flip64File.FixedWidth, height = Flip64File.FixedHeight;
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
    var file = Flip64File.FromRawImage(source);
    var restored = Flip64Reader.FromBytes(Flip64Writer.ToBytes(file));
    var decoded = Flip64File.ToRawImage(restored);
    var decodedBgra = PixelConverter.Convert(decoded, PixelFormat.Bgra32);

    Assert.That(decodedBgra.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsWrongDimensions() {
    var raw = new RawImage { Width = 100, Height = 100, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 100 * 3] };

    Assert.Throws<ArgumentException>(() => Flip64File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_EncodesBothFramesIdentically() {
    var file = Flip64File.FromRawImage(_SolidCells());

    var frame1 = file.RawData.AsSpan(0, Flip64File.BitmapSize + Flip64File.ScreenRamSize).ToArray();
    var frame2 = file.RawData.AsSpan(Flip64File.BitmapSize + Flip64File.ScreenRamSize, Flip64File.BitmapSize + Flip64File.ScreenRamSize).ToArray();

    Assert.That(frame2, Is.EqualTo(frame1));
  }
}
