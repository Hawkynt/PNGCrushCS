using System;
using FileFormat.Core;

namespace FileFormat.IPaint.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _COLUMNS = 3;
  private const int _WIDTH = _COLUMNS * 8;

  /// <summary>Not a multiple of eight, so the last block of colour is a short one.</summary>
  private const int _HEIGHT = 11;

  /// <summary>
  /// Two of the chip's colours per cell and per parity of the row, which is exactly what the format
  /// holds — any more and the picture could not come back unchanged.
  /// </summary>
  private static RawImage _TwoPerHalfCell(int width, int height) {
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var pair = (x >> 3) + (y >> 3) * 2 + (y & 1) * 5;
      var color = IPaintFile.Palette[((x + y) & 1) == 0 ? pair % 16 : (pair + 7) % 16];
      var at = (y * width + x) * 3;
      data[at] = (byte)(color >> 16);
      data[at + 1] = (byte)(color >> 8);
      data[at + 2] = (byte)color;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoColoursPerHalfCell_ReproducesExactly() {
    var source = _TwoPerHalfCell(_WIDTH, _HEIGHT);
    var file = IPaintFile.FromRawImage(source);
    var decoded = IPaintFile.ToRawImage(IPaintReader.FromBytes(IPaintWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAWidthThatIsNotAWholeNumberOfCells() {
    // The width is counted in cells and nothing else, so 37 pixels become 40 rather than a refusal.
    var file = IPaintFile.FromRawImage(_TwoPerHalfCell(37, 11));

    Assert.Multiple(() => {
      Assert.That(file.Columns, Is.EqualTo(5));
      Assert.That(file.Width, Is.EqualTo(40));
      Assert.That(file.Height, Is.EqualTo(11));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ColoursEvenAndOddRowsSeparately() {
    // Two rows of colour serve eight rows of picture and the two alternate down the block, so a cell
    // whose even rows are one pair and whose odd rows are another needs both — reading the block as
    // one pair for all eight rows halves the colours and is the mistake the layout invites.
    var file = IPaintFile.FromRawImage(_TwoPerHalfCell(_WIDTH, _HEIGHT));

    Assert.That(file.Colors[0], Is.Not.EqualTo(file.Colors[_COLUMNS]));
  }
}
