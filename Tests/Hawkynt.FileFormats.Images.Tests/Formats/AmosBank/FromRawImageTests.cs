using System;
using FileFormat.Core;

namespace FileFormat.AmosBank.Tests;

[TestFixture]
public sealed class AmosBankFileFromRawImageTests {

  /// <summary>Thirty-two colours on the four-bit grid an OCS palette stores them in.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x / 2 + y) & 31;
      var at = (y * width + x) * 3;
      rgb[at] = ChannelScaling.Expand4(index & 15);
      rgb[at + 1] = ChannelScaling.Expand4((index >> 1) & 15);
      rgb[at + 2] = ChannelScaling.Expand4(index >= 16 ? 15 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    // A sprite's width counts sixteen-pixel words, so 48 is the awkward width available here — there
    // is no padding to get wrong because the format cannot state a width that would need any.
    var source = _Source(48, 13);
    var decoded = AmosBankFile.ToRawImage(AmosBankFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(48));
      Assert.That(decoded.Height, Is.EqualTo(13));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AWidthTheHardwareCannotFetchIsSampledRatherThanRefused() {
    // Sixteen pixels are one fetch, so 37 across is brought to the nearest width that is whole words.
    var decoded = AmosBankFile.ToRawImage(AmosBankFile.FromRawImage(_Source(37, 9)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(32));
      Assert.That(decoded.Height, Is.EqualTo(9));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AmosBankFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ThePaletteClosesTheBankAndTheWidthCountsWords() {
    // Where the palette sits is what the reader checks the sprite walk against, so a header that
    // miscounted would be caught there rather than in the pixels.
    var bytes = AmosBankWriter.ToBytes(AmosBankFile.FromRawImage(_Source(48, 13)));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'A'));
      Assert.That(bytes[1], Is.EqualTo((byte)'m'));
      Assert.That(bytes[2], Is.EqualTo((byte)'S'));
      Assert.That(bytes[3], Is.EqualTo((byte)'p'));
      Assert.That((bytes[6] << 8) | bytes[7], Is.EqualTo(3));
      Assert.That((bytes[8] << 8) | bytes[9], Is.EqualTo(13));
      Assert.That(bytes[11], Is.EqualTo(AmosBankFile.Planes));
      Assert.That(bytes.Length, Is.EqualTo(16 + AmosBankFile.Planes * 6 * 13 + 64));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = AmosBankFile.FromRawImage(_Source(48, 13));
    var restored = AmosBankReader.FromBytes(AmosBankWriter.ToBytes(file));

    Assert.That(_Rgb(AmosBankFile.ToRawImage(restored)), Is.EqualTo(_Rgb(AmosBankFile.ToRawImage(file))));
  }
}
