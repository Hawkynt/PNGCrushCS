using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PetDraw;

namespace FileFormat.PetDraw.Tests;

[TestFixture]
public sealed class PetDrawTests {

  private static byte[] _Empty() => new byte[PetDrawFile.FileSize];

  private static int _IndexAt(RawImage image, int x, int y) => image.PixelData[y * image.Width + x];

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => PetDrawReader.FromBytes(new byte[PetDrawFile.FileSize - 1]));
      Assert.Throws<InvalidDataException>(() => PetDrawReader.FromBytes(new byte[PetDrawFile.FileSize + 1]));
    });
  }

  [Test]
  public void Dimensions_AreTheTextScreenAtEightByEight() {
    var image = PetDrawFile.ToRawImage(PetDrawReader.FromBytes(_Empty()));

    Assert.That((image.Width, image.Height), Is.EqualTo((320, 200)));
  }

  [Test]
  public void EveryCellPicksItsOwnColor_UnlikeAtariGraphicsZero() {
    var data = _Empty();
    data[PetDrawFile.BackgroundOffset] = 0;
    data[PetDrawFile.ScreenOffset] = 128;       // inverse space: a solid block
    data[PetDrawFile.ScreenOffset + 1] = 128;
    data[PetDrawFile.ColorsOffset] = 5;
    data[PetDrawFile.ColorsOffset + 1] = 9;     // a different colour in the very next cell

    var image = PetDrawFile.ToRawImage(PetDrawReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(_IndexAt(image, 0, 0), Is.EqualTo(5));
      Assert.That(_IndexAt(image, 8, 0), Is.EqualTo(9));
    });
  }

  [Test]
  public void TheBackgroundIsSharedByTheWholeScreen() {
    var data = _Empty();
    data[PetDrawFile.BackgroundOffset] = 6;

    var image = PetDrawFile.ToRawImage(PetDrawReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(_IndexAt(image, 0, 0), Is.EqualTo(6));
      Assert.That(_IndexAt(image, 319, 199), Is.EqualTo(6));
    });
  }

  [Test]
  public void OnlyTheLowNibbleOfAColorIsUsed() {
    var data = _Empty();
    data[PetDrawFile.BackgroundOffset] = 0xF6;

    Assert.That(_IndexAt(PetDrawFile.ToRawImage(PetDrawReader.FromBytes(data)), 0, 0), Is.EqualTo(6));
  }

  [Test]
  public void TheTopBitInvertsTheGlyphRatherThanSelectingOne() {
    var plain = _Empty();
    var inverse = _Empty();
    plain[PetDrawFile.ScreenOffset] = 1;
    inverse[PetDrawFile.ScreenOffset] = 1 | 128;
    plain[PetDrawFile.ColorsOffset] = inverse[PetDrawFile.ColorsOffset] = 1;

    var a = PetDrawFile.ToRawImage(PetDrawReader.FromBytes(plain));
    var b = PetDrawFile.ToRawImage(PetDrawReader.FromBytes(inverse));

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      Assert.That(_IndexAt(b, x, y), Is.Not.EqualTo(_IndexAt(a, x, y)), $"{x},{y}");
  }

  [Test]
  public void Palette_IsTheMeasuredVicIITable() {
    var image = PetDrawFile.ToRawImage(PetDrawReader.FromBytes(_Empty()));

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(16));
      // Cyan is the duller measured value, not the idealised 0xAAFFEE.
      Assert.That(image.Palette![9..12], Is.EqualTo(new byte[] { 0x70, 0xA4, 0xB2 }));
    });
  }

  [Test]
  public void RowsAdvanceEightScanlines() {
    var data = _Empty();
    data[PetDrawFile.ScreenOffset + PetDrawFile.Columns] = 128;
    data[PetDrawFile.ColorsOffset + PetDrawFile.Columns] = 7;

    var image = PetDrawFile.ToRawImage(PetDrawReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(_IndexAt(image, 0, 8), Is.EqualTo(7));
      Assert.That(_IndexAt(image, 0, 7), Is.Zero);
    });
  }
}
