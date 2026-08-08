using System;
using FileFormat.Core;

namespace FileFormat.AtariIce.Tests;

[TestFixture]
public sealed class AtariIceFileFromRawImageTests {

  /// <summary>
  /// A file in the pairing the encoder writes: two character sets shown against each other in
  /// Graphics 9, one hue a field.
  /// </summary>
  /// <remarks>
  /// Built as a file rather than as a picture because the arrangement is the format's and not
  /// anyone's choice — the same character reaches four places on the screen, twice inverted, so a
  /// picture that a file could hold cannot be drawn without going through one.
  /// </remarks>
  private static AtariIceFile _Handmade() {
    var data = new byte[AtariIceFile.Gtia9PairSize];
    data[0] = AtariIceFile.Gtia9PairMode;
    data[1] = 0x40;
    data[2] = 0xB0;

    for (var character = 0; character < 128; ++character)
    for (var row = 0; row < 8; ++row) {
      var at = character * 8 + row;
      data[3 + at] = (byte)((((character + row) & 15) << 4) | ((character * 3 + row) & 15));
      data[3 + AtariIceFile.CharacterSetSize + at] =
        (byte)((((character * 5 + row * 2) & 15) << 4) | ((character + row * 7) & 15));
    }

    return AtariIceReader.FromBytes(data);
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = AtariIceFile.ToRawImage(_Handmade());
    var decoded = AtariIceFile.ToRawImage(AtariIceFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(AtariIceFile.SheetWidth));
      Assert.That(decoded.Height, Is.EqualTo(AtariIceFile.SheetHeight));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = AtariIceFile.ToRawImage(
      AtariIceFile.FromRawImage(AtariIceFile.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(AtariIceFile.SheetWidth));
      Assert.That(decoded.Height, Is.EqualTo(AtariIceFile.SheetHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void APlainRgbPictureIsAccepted() {
    var rgb = new byte[37 * 23 * 3];
    for (var pixel = 0; pixel < 37 * 23; ++pixel) {
      rgb[pixel * 3] = (byte)(pixel * 7);
      rgb[pixel * 3 + 1] = (byte)(pixel * 3);
      rgb[pixel * 3 + 2] = (byte)(pixel * 11);
    }

    var file = AtariIceFile.FromRawImage(
      new() { Width = 37, Height = 23, Format = PixelFormat.Rgb24, PixelData = rgb });

    Assert.That(AtariIceWriter.ToBytes(file), Has.Length.EqualTo(AtariIceFile.Gtia9PairSize));
  }

  [Test]
  [Category("Unit")]
  public void ASolidPictureCannotComeBackSolid() {
    // The quirk the format cannot be argued out of: the second quarter of the screen shows the
    // first quarter's characters inverted, so whichever luminance the first quarter asks for, the
    // second is handed fifteen minus it. Black is therefore unreachable — asking for it everywhere
    // buys a mid grey everywhere, which is the least wrong a picture with a forced negative can be.
    var black = new RawImage {
      Width = AtariIceFile.SheetWidth,
      Height = AtariIceFile.SheetHeight,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[AtariIceFile.SheetWidth * AtariIceFile.SheetHeight * 3],
    };

    var decoded = _Rgb(AtariIceFile.ToRawImage(AtariIceFile.FromRawImage(black)));
    var quarter = AtariIceFile.SheetWidth * (AtariIceFile.SheetHeight / 4) * 3;

    long first = 0, second = 0;
    for (var at = 0; at < quarter; ++at) {
      first += decoded[at];
      second += decoded[quarter + at];
    }

    Assert.Multiple(() => {
      Assert.That(first / (double)quarter, Is.GreaterThan(64.0));
      Assert.That(second / (double)quarter, Is.GreaterThan(64.0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = AtariIceFile.FromRawImage(AtariIceFile.ToRawImage(_Handmade()));
    var bytes = AtariIceWriter.ToBytes(file);
    var restored = AtariIceReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(AtariIceFile.Gtia9PairSize));
      Assert.That(bytes[0], Is.EqualTo(AtariIceFile.Gtia9PairMode));
      Assert.That(_Rgb(AtariIceFile.ToRawImage(restored)), Is.EqualTo(_Rgb(AtariIceFile.ToRawImage(file))));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AtariIceFile.FromRawImage(null!));
}
