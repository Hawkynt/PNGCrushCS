using System;
using FileFormat.Core;

namespace FileFormat.AnimatorCompressor.Tests;

[TestFixture]
public sealed class AnimatorCompressorFileFromRawImageTests {

  /// <summary>
  /// The four luminances of one hue the mode shows, each drawn two pixels wide because that is what
  /// a two-bit pixel covers.
  /// </summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var level = (x / 2 + y) & 3;
      var entry = (level << 2) * 3;
      var at = (y * width + x) * 3;
      rgb[at] = Atari8BitGraphics.Palette[entry];
      rgb[at + 1] = Atari8BitGraphics.Palette[entry + 1];
      rgb[at + 2] = Atari8BitGraphics.Palette[entry + 2];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(40, 24);
    var decoded = AnimatorCompressorFile.ToRawImage(AnimatorCompressorFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(40));
      Assert.That(decoded.Height, Is.EqualTo(24));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASizeThatIsNotWholeTilesIsSampledRatherThanRefused() {
    // The sheet is counted in eight-by-eight tiles, so a picture that is not a whole number of them
    // is brought to the nearest that is.
    var decoded = AnimatorCompressorFile.ToRawImage(AnimatorCompressorFile.FromRawImage(_Source(37, 21)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(40));
      Assert.That(decoded.Height, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AnimatorCompressorFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void IdenticalCellsShareOneTile() {
    // Naming a tile costs a byte where drawing it costs eight, which is the whole of the format's
    // reason to exist: a picture of four repeating stripes is said in a handful of tiles.
    var file = AnimatorCompressorFile.FromRawImage(_Source(40, 24));
    var bytes = AnimatorCompressorWriter.ToBytes(file);
    var tiles = (bytes.Length - AnimatorCompressorFile.MapOffset - 15) / AnimatorCompressorFile.TileLength;

    Assert.Multiple(() => {
      Assert.That(file.Frames, Is.EqualTo(1));
      Assert.That(file.Columns, Is.EqualTo(5));
      Assert.That(file.Rows, Is.EqualTo(3));
      Assert.That(tiles, Is.LessThan(15));

      // The whole of the signature is that the file is an Atari executable.
      Assert.That(bytes[0], Is.EqualTo(255));
      Assert.That(bytes[1], Is.EqualTo(255));
      Assert.That(6 + (bytes[4] | (bytes[5] << 8)) - (bytes[2] | (bytes[3] << 8)) + 1, Is.EqualTo(bytes.Length));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = AnimatorCompressorFile.FromRawImage(_Source(40, 24));
    var restored = AnimatorCompressorReader.FromBytes(AnimatorCompressorWriter.ToBytes(file));

    Assert.That(
      _Rgb(AnimatorCompressorFile.ToRawImage(restored)),
      Is.EqualTo(_Rgb(AnimatorCompressorFile.ToRawImage(file))));
  }
}
