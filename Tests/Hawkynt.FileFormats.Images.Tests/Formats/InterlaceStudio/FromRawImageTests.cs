using System;
using FileFormat.Core;

namespace FileFormat.InterlaceStudio.Tests;

[TestFixture]
public sealed class InterlaceStudioFileFromRawImageTests {

  /// <summary>The GTIA registers the probe is drawn in: black and three the palette holds exactly.</summary>
  private static readonly byte[] _Registers = [0x00, 0x24, 0x88, 0x0E];

  /// <summary>
  /// Four colours, each stored pixel drawn two wide, which is exactly what one mode E screen holds.
  /// </summary>
  private static RawImage _Source(int width, int height) {
    var palette = Atari8BitGraphics.Palette;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var entry = _Registers[(x / 2 + y / 8) % _Registers.Length] * 3;
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
    var source = _Source(320, 200);
    var decoded = InterlaceStudioFile.ToRawImage(InterlaceStudioFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = InterlaceStudioFile.ToRawImage(InterlaceStudioFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegisterTablesAreWritten() {
    var file = InterlaceStudioFile.FromRawImage(_Source(320, 200));

    Assert.That(file.Registers, Has.Length.EqualTo(
      InterlaceStudioFile.RegisterTableCount * InterlaceStudioFile.RegisterTableSize));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => InterlaceStudioFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = InterlaceStudioFile.FromRawImage(_Source(320, 200));
    var restored = InterlaceStudioReader.FromBytes(InterlaceStudioWriter.ToBytes(file));

    Assert.That(_Rgb(InterlaceStudioFile.ToRawImage(restored)), Is.EqualTo(_Rgb(InterlaceStudioFile.ToRawImage(file))));
  }
}
