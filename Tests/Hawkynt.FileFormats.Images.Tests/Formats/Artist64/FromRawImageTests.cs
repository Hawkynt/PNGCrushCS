using System;
using FileFormat.Core;

namespace FileFormat.Artist64.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Each 4x8 cell is solid one of four exact VIC-II colours, cycling by cell index — well
  /// inside the four-colours-per-cell budget, so quantization and cell selection are both exact.</summary>
  private static RawImage _SolidCells() {
    const int width = Artist64File.FixedWidth, height = Artist64File.FixedHeight;
    ReadOnlySpan<int> palette = [0, 1, 2, 3]; // black, white, red, cyan indices into HexColors
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
    var file = Artist64File.FromRawImage(source);
    var restored = Artist64Reader.FromBytes(Artist64Writer.ToBytes(file));
    var decoded = Artist64File.ToRawImage(restored);
    var decodedBgra = PixelConverter.Convert(decoded, PixelFormat.Bgra32);

    Assert.That(decodedBgra.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsWrongDimensions() {
    var raw = new RawImage { Width = 100, Height = 100, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 100 * 3] };

    Assert.Throws<ArgumentException>(() => Artist64File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesTheStandardSectionSizes() {
    var file = Artist64File.FromRawImage(_SolidCells());

    Assert.That(file.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(file.VideoMatrix.Length, Is.EqualTo(1000));
    Assert.That(file.ColorRam.Length, Is.EqualTo(1000));
  }
}
