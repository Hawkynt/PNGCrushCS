using System;
using System.IO;
using FileFormat.AtariTools800Font;
using FileFormat.Core;

namespace FileFormat.AtariTools800Font.Tests;

[TestFixture]
public sealed class AtariTools800FontTests {

  /// <summary>Four vertical bands of flat colour, one per pixel value the mode offers.</summary>
  private static RawImage _Bands() {
    const int width = AtariTools800FontFile.DisplayWidth;
    const int height = AtariTools800FontFile.DisplayHeight;
    byte[][] colors = [[10, 10, 10], [220, 20, 20], [20, 220, 20], [220, 220, 220]];
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      var band = colors[x / (width / 4)];
      data[o + 2] = band[0];
      data[o + 1] = band[1];
      data[o] = band[2];
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_IsFourColorsAndAHundredAndTwentyEightGlyphs()
    => Assert.That(AtariTools800FontFile.FileSize, Is.EqualTo(1028));

  [Test]
  [Category("Unit")]
  public void TheSheetHasOneCellPerGlyph()
    => Assert.That(AtariTools800FontFile.Columns * AtariTools800FontFile.Rows, Is.EqualTo(AtariTools800FontFile.GlyphCount));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheColorsAndCharacterSet() {
    var file = AtariTools800FontFile.FromRawImage(_Bands());
    var restored = AtariTools800FontReader.FromBytes(AtariTools800FontWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Colors, Is.EqualTo(file.Colors));
      Assert.That(restored.FontData, Is.EqualTo(file.FontData));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesTheBandsExactly() {
    // Every cell has a glyph to itself, so nothing here has to be approximated: the same pixel
    // value must come back in every position.
    var decoded = AtariTools800FontFile.ToRawImage(AtariTools800FontFile.FromRawImage(_Bands()));
    const int width = AtariTools800FontFile.DisplayWidth;

    for (var y = 0; y < AtariTools800FontFile.DisplayHeight; ++y)
    for (var x = 0; x < width; ++x)
      Assert.That(decoded.PixelData[y * width + x], Is.EqualTo(decoded.PixelData[x / (width / 4) * (width / 4)]), $"pixel {x},{y}");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheSheetSize() {
    var raw = AtariTools800FontFile.ToRawImage(AtariTools800FontFile.FromRawImage(_Bands()));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(128));
      Assert.That(raw.Height, Is.EqualTo(64));
      Assert.That(raw.PaletteCount, Is.EqualTo(AtariTools800FontFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAWronglySizedFile()
    => Assert.Throws<InvalidDataException>(() => AtariTools800FontReader.FromBytes(new byte[1024]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 128, Height = 128, Format = PixelFormat.Bgra32, PixelData = new byte[128 * 128 * 4] };

    Assert.Throws<ArgumentException>(() => AtariTools800FontFile.FromRawImage(raw));
  }
}
