using System;
using FileFormat.Core;

namespace FileFormat.IcePcinPlus.Tests;

[TestFixture]
public sealed class IcePcinPlusFileFromRawImageTests {

  /// <summary>The thirteen colours the handmade picture is drawn from, all of different hue.</summary>
  private static ReadOnlySpan<byte> _Colors => [0x00, 0x28, 0x4A, 0x6C, 0x8E, 0x1A, 0x36, 0x52, 0x7E, 0x9A, 0xB6, 0xD2, 0xEE];

  /// <summary>
  /// A picture built as a file, since a cell's two fields read one character code out of two
  /// character sets and no drawing says what either set should hold.
  /// </summary>
  /// <remarks>
  /// Both character sets are filled with something that repeats on no useful period, and the screen
  /// alternates the character code's high bit, so both of field one's fourth colours are in play and
  /// nothing the encoder recovers can be recovered by accident.
  /// </remarks>
  private static IcePcinPlusFile _Handmade() {
    var data = new byte[IcePcinPlusFile.FileSize];
    data[0] = 1;
    _Colors.CopyTo(data.AsSpan(1));

    for (var at = 14; at < IcePcinPlusFile.ScreenOffset; ++at)
      data[at] = (byte)(at * 37 ^ at >> 5);

    // A hundred and twenty cells a block against the hundred and twenty-eight characters a block can
    // name, so every cell gets one to itself.
    for (var cell = 0; cell < 960; ++cell)
      data[IcePcinPlusFile.ScreenOffset + cell] = (byte)((cell & 1) << 7 | cell % 120);

    return IcePcinPlusReader.FromBytes(data);
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = IcePcinPlusFile.ToRawImage(_Handmade());
    var decoded = IcePcinPlusFile.ToRawImage(IcePcinPlusFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(IcePcinPlusFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(IcePcinPlusFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = IcePcinPlusFile.ToRawImage(
      IcePcinPlusFile.FromRawImage(IcePcinPlusFile.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(IcePcinPlusFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(IcePcinPlusFile.Height));
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

    var file = IcePcinPlusFile.FromRawImage(
      new() { Width = 37, Height = 23, Format = PixelFormat.Rgb24, PixelData = rgb });

    Assert.That(IcePcinPlusWriter.ToBytes(file), Has.Length.EqualTo(IcePcinPlusFile.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void ColumnsCannotChangeExceptInPairs() {
    // The quirk: the narrower of the two fields is a mode 12 pixel, which is two screen pixels, and
    // the other is a GTIA 10 pixel, which is four. Whatever the picture asks for, no odd column can
    // differ from the even one before it — so detail finer than a pair is not lost to the encoder's
    // choices but to what the modes can address at all.
    var rgb = new byte[IcePcinPlusFile.Width * IcePcinPlusFile.Height * 3];
    for (var y = 0; y < IcePcinPlusFile.Height; ++y)
    for (var x = 0; x < IcePcinPlusFile.Width; ++x) {
      var at = (y * IcePcinPlusFile.Width + x) * 3;
      rgb[at] = (byte)(x * 8);
      rgb[at + 1] = (byte)(y * 5 + x * 3);
      rgb[at + 2] = (byte)(x % 2 * 255);
    }

    var decoded = _Rgb(IcePcinPlusFile.ToRawImage(IcePcinPlusFile.FromRawImage(
      new() { Width = IcePcinPlusFile.Width, Height = IcePcinPlusFile.Height, Format = PixelFormat.Rgb24, PixelData = rgb })));

    for (var y = 0; y < IcePcinPlusFile.Height; ++y)
    for (var x = 0; x < IcePcinPlusFile.Width; x += 2) {
      var at = (y * IcePcinPlusFile.Width + x) * 3;
      Assert.That(decoded.AsSpan(at + 3, 3).SequenceEqual(decoded.AsSpan(at, 3)), Is.True, $"{x},{y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void OneByteIsBothTheBackgroundAndTheFirstPlayer() {
    // The other quirk, and the one that makes the thirteen colours a picture-wide choice rather than
    // a cell's: the second byte of the file is field one's background and field two's first player at
    // once. An encoder that settled the two fields separately would want two different values there.
    var file = IcePcinPlusFile.FromRawImage(IcePcinPlusFile.ToRawImage(_Handmade()));
    var bytes = IcePcinPlusWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(file.Fields[0].Registers[8], Is.EqualTo(bytes[1] & 254));
      Assert.That(file.Fields[1].Registers[0], Is.EqualTo(bytes[1] & 254));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = IcePcinPlusFile.FromRawImage(IcePcinPlusFile.ToRawImage(_Handmade()));
    var bytes = IcePcinPlusWriter.ToBytes(file);
    var restored = IcePcinPlusReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(IcePcinPlusFile.FileSize));
      Assert.That(bytes[0], Is.EqualTo(1));
      Assert.That(_Rgb(IcePcinPlusFile.ToRawImage(restored)), Is.EqualTo(_Rgb(IcePcinPlusFile.ToRawImage(file))));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => IcePcinPlusFile.FromRawImage(null!));
}
