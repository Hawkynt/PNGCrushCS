using System;
using System.IO;
using FileFormat.Core;
using FileFormat.GunPaint;

namespace FileFormat.GunPaint.Tests;

[TestFixture]
public sealed class GunPaintTests {

  private static byte[] _Empty() => new byte[GunPaintFile.FileSize];

  private static (byte R, byte G, byte B) _PixelAt(RawImage image, int x, int y) {
    var o = (y * image.Width + x) * 3;
    return (image.PixelData[o], image.PixelData[o + 1], image.PixelData[o + 2]);
  }

  [Test]
  public void Reader_AcceptsBothStoredSizes() {
    Assert.Multiple(() => {
      Assert.That(GunPaintReader.FromBytes(_Empty()).Data, Is.Not.Null);
      Assert.That(GunPaintReader.FromBytes(new byte[GunPaintFile.FileSize + 1]).Data, Is.Not.Null);
      Assert.Throws<InvalidDataException>(() => GunPaintReader.FromBytes(new byte[GunPaintFile.FileSize - 1]));
    });
  }

  [Test]
  public void TheRasterWorkCostsTheLeftmostCharacters() {
    var image = GunPaintFile.ToRawImage(GunPaintReader.FromBytes(_Empty()));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(296), "296 rather than 320");
      Assert.That(image.Height, Is.EqualTo(200));
      // Two fields averaged, so the result is colour rather than indices.
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  public void TheBackgroundTableIsInThreePieces() {
    Assert.Multiple(() => {
      // Most of the screen, then twenty lines from elsewhere, then one byte for the rest.
      Assert.That(GunPaintFile.BackgroundOffsetFor(0), Is.EqualTo(16209));
      Assert.That(GunPaintFile.BackgroundOffsetFor(176), Is.EqualTo(16209 + 176));
      Assert.That(GunPaintFile.BackgroundOffsetFor(177), Is.EqualTo(18233 + 177));
      Assert.That(GunPaintFile.BackgroundOffsetFor(196), Is.EqualTo(18233 + 196));
      Assert.That(GunPaintFile.BackgroundOffsetFor(197), Is.EqualTo(18429));
      Assert.That(GunPaintFile.BackgroundOffsetFor(199), Is.EqualTo(18429));
    });
  }

  [Test]
  public void EachScanlineTakesItsOwnBackground() {
    var data = _Empty();
    data[GunPaintFile.BackgroundOffsetFor(0)] = 2;
    data[GunPaintFile.BackgroundOffsetFor(1)] = 5;

    var image = GunPaintFile.ToRawImage(GunPaintReader.FromBytes(data));
    var palette = Commodore64Graphics.CreatePalette();

    Assert.Multiple(() => {
      Assert.That(_PixelAt(image, 8, 0), Is.EqualTo((palette[6], palette[7], palette[8])));
      Assert.That(_PixelAt(image, 8, 1), Is.EqualTo((palette[15], palette[16], palette[17])));
    });
  }

  [Test]
  public void EachRowOfACellGetsItsOwnVideoMatrix() {
    var data = _Empty();
    // Pattern 01 in both fields at the first cell, on rows 0 and 1 of the cell.
    data[GunPaintFile.FirstBitmapOffset] = 0b0100_0000;
    data[GunPaintFile.FirstBitmapOffset + 1] = 0b0100_0000;
    data[GunPaintFile.SecondBitmapOffset] = 0b0100_0000;
    data[GunPaintFile.SecondBitmapOffset + 1] = 0b0100_0000;

    // The two rows read different matrices, a kilobyte apart.
    data[GunPaintFile.FirstMatrixOffset] = 0x30;
    data[GunPaintFile.FirstMatrixOffset + GunPaintFile.MatrixStride] = 0x70;
    data[GunPaintFile.SecondMatrixOffset] = 0x30;
    data[GunPaintFile.SecondMatrixOffset + GunPaintFile.MatrixStride] = 0x70;

    var image = GunPaintFile.ToRawImage(GunPaintReader.FromBytes(data));

    Assert.That(_PixelAt(image, 1, 1), Is.Not.EqualTo(_PixelAt(image, 1, 0)),
      "a FLI screen changes colours between the rows of one cell");
  }

  [Test]
  public void TheSecondFieldIsDisplacedByOnePixel() {
    Assert.That(GunPaintFile.SecondFieldShift, Is.EqualTo(1));

    var data = _Empty();
    // Pattern 01 across the first cell of the second field only, so the colour comes from the
    // video matrix rather than the colour RAM, which is zero here.
    data[GunPaintFile.SecondBitmapOffset] = 0b0101_0101;
    data[GunPaintFile.SecondMatrixOffset] = 0x70;

    var image = GunPaintFile.ToRawImage(GunPaintReader.FromBytes(data));

    // Column 0 of the displaced field has nothing to show and falls back to the background, so it
    // differs from column 1 where the field does reach.
    Assert.That(_PixelAt(image, 0, 0), Is.Not.EqualTo(_PixelAt(image, 1, 0)));
  }
}
