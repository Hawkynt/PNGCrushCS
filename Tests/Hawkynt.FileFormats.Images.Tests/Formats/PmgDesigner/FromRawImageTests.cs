using System;
using FileFormat.Core;

namespace FileFormat.PmgDesigner.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const byte _FIRST = 0x28;
  private const byte _SECOND = 0x86;

  /// <summary>
  /// A sheet of shapes in the four colours a pair of players makes, with the four pixels a cell does
  /// not cover and the two scanlines between rows left as background.
  /// </summary>
  private static RawImage _Sheet(int rows) {
    var step = PmgDesignerFile.WrittenHeight + PmgDesignerFile.RowGap;
    var height = rows * step - PmgDesignerFile.RowGap;
    var gtia = Atari8BitGraphics.Palette;
    var data = new byte[PmgDesignerFile.FullWidth * height * 3];

    for (var y = 0; y < height; ++y) {
      if (y % step >= PmgDesignerFile.WrittenHeight)
        continue;

      for (var x = 0; x < PmgDesignerFile.FullWidth; ++x) {
        if (x % PmgDesignerFile.CellWidth >= PmgDesignerFile.PairWidth)
          continue;

        var value = ((x >> 1) + y) & 3;
        var color = ((value & 1) != 0 ? _FIRST : 0) | ((value & 2) != 0 ? _SECOND : 0);
        var entry = color * 3;
        var at = (y * PmgDesignerFile.FullWidth + x) * 3;
        data[at] = gtia[entry];
        data[at + 1] = gtia[entry + 1];
        data[at + 2] = gtia[entry + 2];
      }
    }

    return new() {
      Width = PmgDesignerFile.FullWidth, Height = height, Format = PixelFormat.Rgb24, PixelData = data,
    };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AGridOfShapes_ReproducesExactly() {
    var source = _Sheet(2);
    var file = PmgDesignerFile.FromRawImage(source);
    var decoded = PmgDesignerFile.ToRawImage(PmgDesignerReader.FromBytes(PmgDesignerWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((source.Width, source.Height)));
      Assert.That(file.Cells, Is.EqualTo(2 * PmgDesignerFile.CellsPerRow));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ChoosesTheGridNearestThePictureAndSamplesToIt() {
    // A sheet's size follows from how many shapes it holds, so a picture of any other shape gets the
    // grid nearest it rather than a refusal.
    var wide = PmgDesignerFile.FromRawImage(_Sheet(1));
    var tall = PmgDesignerFile.FromRawImage(new RawImage {
      Width = 37, Height = 400, Format = PixelFormat.Rgb24, PixelData = new byte[37 * 400 * 3],
    });

    Assert.Multiple(() => {
      Assert.That(wide.Cells, Is.EqualTo(PmgDesignerFile.CellsPerRow));
      Assert.That(tall.Cells, Is.EqualTo(PmgDesignerFile.MaximumShapes));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesTwoSpritesBecauseACellIsAPair() {
    // The shape count in the header is twice what appears: sprites are stored and drawn in pairs,
    // and a sheet of one sprite would leave every cell drawn by half of itself.
    var file = PmgDesignerFile.FromRawImage(_Sheet(1));

    Assert.Multiple(() => {
      Assert.That(file.Data[7], Is.EqualTo(2));
      Assert.That(
        file.Data,
        Has.Length.EqualTo(PmgDesignerFile.ShapesOffset + 2 * file.Cells * PmgDesignerFile.WrittenHeight));
    });
  }
}
