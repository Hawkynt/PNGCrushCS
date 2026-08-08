using System;
using FileFormat.Core;

namespace FileFormat.LudekMaker.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const byte _FIRST = 0x28;
  private const byte _SECOND = 0x86;

  /// <summary>
  /// A sheet of figures in the four colours two overlapping players make, with the eight pixels a
  /// cell does not cover and the two scanlines between rows left as background — which is what the
  /// grid can hold and nothing more.
  /// </summary>
  private static RawImage _Sheet(int rows) {
    var height = rows * LudekMakerFile.RowHeight - 2;
    var gtia = Atari8BitGraphics.Palette;
    var data = new byte[LudekMakerFile.FullWidth * height * 3];

    for (var y = 0; y < height; ++y) {
      if (y % LudekMakerFile.RowHeight >= LudekMakerFile.FigureHeight)
        continue;

      for (var x = 0; x < LudekMakerFile.FullWidth; ++x) {
        if (x % LudekMakerFile.CellWidth >= LudekMakerFile.FigureWidth)
          continue;

        var value = ((x >> 1) + y) & 3;
        var color = ((value & 1) != 0 ? _FIRST : 0) | ((value & 2) != 0 ? _SECOND : 0);
        var entry = color * 3;
        var at = (y * LudekMakerFile.FullWidth + x) * 3;
        data[at] = gtia[entry];
        data[at + 1] = gtia[entry + 1];
        data[at + 2] = gtia[entry + 2];
      }
    }

    return new() {
      Width = LudekMakerFile.FullWidth, Height = height, Format = PixelFormat.Rgb24, PixelData = data,
    };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AGridOfFigures_ReproducesExactly() {
    var source = _Sheet(2);
    var file = LudekMakerFile.FromRawImage(source);
    var decoded = LudekMakerFile.ToRawImage(LudekMakerReader.FromBytes(LudekMakerWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((source.Width, source.Height)));
      Assert.That(file.Shapes, Is.EqualTo(16));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ChoosesTheGridNearestThePictureAndSamplesToIt() {
    // A sheet's size follows from how many figures it holds, so a picture of any other shape is
    // given the grid nearest it rather than refused.
    var wide = LudekMakerFile.FromRawImage(_Sheet(1));
    var tall = LudekMakerFile.FromRawImage(new RawImage {
      Width = 37, Height = 400, Format = PixelFormat.Rgb24, PixelData = new byte[37 * 400 * 3],
    });

    Assert.Multiple(() => {
      Assert.That(wide.Shapes, Is.EqualTo(LudekMakerFile.CellsPerRow));
      Assert.That(tall.Shapes, Is.EqualTo(LudekMakerFile.MaximumShapes / LudekMakerFile.CellsPerRow * LudekMakerFile.CellsPerRow));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesTheSecondPairEightPixelsAlongTheFirst() {
    // A figure is four players rather than one, the second pair sitting sixteen screen pixels right
    // of the first — reading it as one pair halves the figure and is what the layout invites.
    var file = LudekMakerFile.FromRawImage(_Sheet(1));
    var first = file.Data[LudekMakerFile.ShapesOffset..(LudekMakerFile.ShapesOffset + LudekMakerFile.FigureHeight)];
    var third = file.Data[
      (LudekMakerFile.ShapesOffset + LudekMakerFile.FigureHeight * 2)
      ..(LudekMakerFile.ShapesOffset + LudekMakerFile.FigureHeight * 3)];

    Assert.Multiple(() => {
      Assert.That(first, Is.Not.All.Zero);
      Assert.That(third, Is.Not.All.Zero);
    });
  }
}
