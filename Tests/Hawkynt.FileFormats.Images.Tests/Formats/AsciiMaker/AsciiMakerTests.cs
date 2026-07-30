using System;
using System.IO;
using FileFormat.AsciiMaker;
using FileFormat.Core;

namespace FileFormat.AsciiMaker.Tests;

[TestFixture]
public sealed class AsciiMakerTests {

  [Test]
  public void BothAcceptedLengthsGiveTheSameScreen() {
    var exact = new byte[AsciiMakerFile.ScreenSize];
    var padded = new byte[AsciiMakerFile.PaddedSize];
    for (var i = 0; i < AsciiMakerFile.ScreenSize; ++i)
      exact[i] = padded[i] = (byte)(i * 7);

    Assert.That(AsciiMakerReader.FromBytes(padded).Characters, Is.EqualTo(AsciiMakerReader.FromBytes(exact).Characters));
  }

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => AsciiMakerReader.FromBytes(new byte[959]));
      Assert.Throws<InvalidDataException>(() => AsciiMakerReader.FromBytes(new byte[1025]));
    });
  }

  [Test]
  public void Dimensions_AreTheTextScreenAtEightByEight() {
    var image = AsciiMakerFile.ToRawImage(AsciiMakerReader.FromBytes(new byte[AsciiMakerFile.ScreenSize]));

    Assert.That((image.Width, image.Height), Is.EqualTo((320, 192)));
  }

  [Test]
  public void TheTopBitInvertsTheGlyphRatherThanSelectingOne() {
    var plain = new byte[AsciiMakerFile.ScreenSize];
    var inverse = new byte[AsciiMakerFile.ScreenSize];
    plain[0] = 33;          // an exclamation mark
    inverse[0] = 33 | 128;  // the same glyph, inverted

    var a = AsciiMakerFile.ToRawImage(AsciiMakerReader.FromBytes(plain));
    var b = AsciiMakerFile.ToRawImage(AsciiMakerReader.FromBytes(inverse));

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var i = y * AsciiMakerFile.Width + x;
      Assert.That(b.PixelData[i], Is.EqualTo(1 - a.PixelData[i]), $"{x},{y}");
    }
  }

  [Test]
  public void TheForegroundKeepsOnlyItsLuminance() {
    // Graphics 0 takes the hue from the background register, so the two colours can never differ
    // in hue — only in brightness. That is a property of the mode, not of this file.
    var (background, foreground) = AsciiMakerFile.Colors;

    Assert.Multiple(() => {
      Assert.That(foreground & 240, Is.EqualTo(background & 240), "hue must come from the background");
      Assert.That(foreground & 14, Is.EqualTo(AsciiMakerFile.ForegroundColor & 14), "luminance from the foreground");
    });
  }

  [Test]
  public void ASpaceLeavesTheCellBlank() {
    var data = new byte[AsciiMakerFile.ScreenSize];
    var image = AsciiMakerFile.ToRawImage(AsciiMakerReader.FromBytes(data));

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      Assert.That(image.PixelData[y * AsciiMakerFile.Width + x], Is.Zero, $"{x},{y}");
  }

  [Test]
  public void CharactersAdvanceEightPixelsAndRowsEightScanlines() {
    var data = new byte[AsciiMakerFile.ScreenSize];
    data[1] = 128;                        // inverse space: a solid block in cell 1
    data[AsciiMakerFile.Columns] = 128;   // and the first cell of the next row

    var image = AsciiMakerFile.ToRawImage(AsciiMakerReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.PixelData[8], Is.EqualTo(1), "cell 1 starts at x=8");
      Assert.That(image.PixelData[0], Is.Zero);
      Assert.That(image.PixelData[8 * AsciiMakerFile.Width], Is.EqualTo(1), "row 1 starts at y=8");
    });
  }
}
