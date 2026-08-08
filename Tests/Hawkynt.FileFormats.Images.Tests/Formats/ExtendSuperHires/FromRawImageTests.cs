using System;
using FileFormat.Core;

namespace FileFormat.ExtendSuperHires.Tests;

[TestFixture]
public sealed class ExtendSuperHiresFileFromRawImageTests {

  /// <summary>
  /// Two colours a cell and the average of the two, which is the whole of what two hires fields over
  /// one colour map can show.
  /// </summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    var palette = Commodore64Graphics.CreatePalette();

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = (y >> 3) * (width >> 3) + (x >> 3);
      var high = cell % Commodore64Graphics.ColorCount;
      var low = (cell / Commodore64Graphics.ColorCount + 3) % Commodore64Graphics.ColorCount;
      var shown = (x + y) % 3;

      var at = (y * width + x) * 3;
      for (var channel = 0; channel < 3; ++channel) {
        int a = palette[high * 3 + channel], b = palette[low * 3 + channel];
        rgb[at + channel] = shown switch {
          0 => (byte)a,
          1 => (byte)b,
          _ => (byte)((a & b) + (((a ^ b) >> 1) & 0x7F)),
        };
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(ExtendSuperHiresFile.Width, ExtendSuperHiresFile.Height);
    var decoded = ExtendSuperHiresFile.ToRawImage(ExtendSuperHiresFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(ExtendSuperHiresFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(ExtendSuperHiresFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // 192 by 200 is eight sprites across and just under ten down, which is what a C64 shows without
    // multiplexing; a caller has whatever it has.
    var decoded = ExtendSuperHiresFile.ToRawImage(ExtendSuperHiresFile.FromRawImage(_Source(104, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(ExtendSuperHiresFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(ExtendSuperHiresFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => ExtendSuperHiresFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheTwoFramesDifferWhereACellShowsTheMixtureAndNoSpritesAreWritten() {
    // Frames that agreed everywhere would show only the two colours the map names, and the third —
    // the average of them — is what the interlacing is for.
    var bytes = ExtendSuperHiresWriter.ToBytes(
      ExtendSuperHiresFile.FromRawImage(_Source(ExtendSuperHiresFile.Width, ExtendSuperHiresFile.Height)));

    var differing = 0;
    for (var i = 0; i < 4800; ++i)
      if (bytes[ExtendSuperHiresFile.FirstBitmapOffset + i] != bytes[ExtendSuperHiresFile.SecondBitmapOffset + i])
        ++differing;

    var sprites = 0;
    for (var i = ExtendSuperHiresFile.FirstSpriteOffset; i < ExtendSuperHiresFile.ColorMapOffset; ++i)
      sprites |= bytes[i];

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(ExtendSuperHiresFile.UnpackedFileSize));
      Assert.That(bytes[2], Is.EqualTo(0));
      Assert.That(differing, Is.GreaterThan(0));
      Assert.That(sprites, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = ExtendSuperHiresFile.FromRawImage(_Source(ExtendSuperHiresFile.Width, ExtendSuperHiresFile.Height));
    var restored = ExtendSuperHiresReader.FromBytes(ExtendSuperHiresWriter.ToBytes(file));

    Assert.That(
      _Rgb(ExtendSuperHiresFile.ToRawImage(restored)), Is.EqualTo(_Rgb(ExtendSuperHiresFile.ToRawImage(file))));
  }
}
