using System;
using FileFormat.Core;

namespace FileFormat.FunWithArt.Tests;

[TestFixture]
public sealed class FunWithArtFileFromRawImageTests {

  /// <summary>
  /// Four colours a row and a different four every row, each logical pixel two screen pixels wide.
  /// </summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    var palette = Atari8BitGraphics.Palette;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      // Four registers spread across the hue range, moved on by one hue every row.
      var register = (x / 2) & 3;
      var entry = ((((y & 15) + register * 4) & 15) << 4 | 8) * 3;
      var at = (y * width + x) * 3;
      rgb[at] = palette[entry];
      rgb[at + 1] = palette[entry + 1];
      rgb[at + 2] = palette[entry + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(FunWithArtFile.Width, FunWithArtFile.Height);
    var decoded = FunWithArtFile.ToRawImage(FunWithArtFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FunWithArtFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(FunWithArtFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = FunWithArtFile.ToRawImage(FunWithArtFile.FromRawImage(_Source(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FunWithArtFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(FunWithArtFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FunWithArtFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheColourChangesAreTheRoutinesThatPerformThemAndTheBitmapSkipsTheDisplayListsGap() {
    // Nothing in the file is a table of colours: a row whose colours differ raises an interrupt and
    // the routine pokes what changed, so the file's length depends on how often the colours move.
    var bytes = FunWithArtWriter.ToBytes(FunWithArtFile.FromRawImage(_Source(FunWithArtFile.Width, FunWithArtFile.Height)));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(254));
      Assert.That(bytes[11], Is.EqualTo(80));
      Assert.That(bytes[115], Is.EqualTo(96));
      Assert.That(bytes[205], Is.EqualTo(65));

      // Row zero and row 102 each load a bitmap address; the rows between are plain four-colour lines.
      Assert.That(bytes[FunWithArtFile.DisplayListOffset] & 127, Is.EqualTo(78));
      Assert.That(bytes[113] & 127, Is.EqualTo(78));
      Assert.That(bytes[50] & 127, Is.EqualTo(14));

      Assert.That(
        FunWithArtFile.InterruptOffset + bytes[7958] + (bytes[7959] << 8), Is.EqualTo(bytes.Length));
      Assert.That(bytes.Length, Is.GreaterThan(FunWithArtFile.InterruptOffset));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FunWithArtFile.FromRawImage(_Source(FunWithArtFile.Width, FunWithArtFile.Height));
    var restored = FunWithArtReader.FromBytes(FunWithArtWriter.ToBytes(file));

    Assert.That(_Rgb(FunWithArtFile.ToRawImage(restored)), Is.EqualTo(_Rgb(FunWithArtFile.ToRawImage(file))));
  }
}
