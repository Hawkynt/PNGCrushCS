using System;
using FileFormat.Core;

namespace FileFormat.AtariHardInterlace.Tests;

[TestFixture]
public sealed class AtariHardInterlaceFileFromRawImageTests {

  private const int _HEIGHT = 96;

  /// <summary>
  /// A picture built by decoding a file, which is the only way to be sure it is one the format can
  /// hold: what a pixel shows is a grey averaged with a register colour, and the two edge columns
  /// are what the displacement leaves behind.
  /// </summary>
  private static RawImage _Source() => AtariHardInterlaceFile.ToRawImage(_Handmade());

  private static AtariHardInterlaceFile _Handmade() {
    var fieldSize = _HEIGHT * AtariHardInterlaceFile.RowStride;
    var luminances = new byte[fieldSize];
    var colors = new byte[fieldSize];
    var registers = new byte[AtariHardInterlaceFile.RegisterBlockSize];

    // The first entry is what the left edge falls back to and is left black; the rest are four
    // colours well apart from each other, leaving the picture inside the nine registers a file has
    // even when the encoder spends some of them elsewhere.
    ReadOnlySpan<byte> chosen = [0x26, 0x58, 0x8A, 0xB6];
    for (var i = 0; i < chosen.Length; ++i)
      registers[i + 1] = chosen[i];

    for (var y = 0; y < _HEIGHT; ++y) {
      var luminance = (y / 4) & 15;
      var entry = 1 + (y / 3) % chosen.Length;

      for (var nibble = 0; nibble < AtariHardInterlaceFile.NibblesPerRow; ++nibble) {
        var shift = (nibble & 1) == 0 ? 4 : 0;
        luminances[y * AtariHardInterlaceFile.RowStride + (nibble >> 1)] |= (byte)(luminance << shift);
        colors[y * AtariHardInterlaceFile.RowStride + (nibble >> 1)] |= (byte)(entry << shift);
      }
    }

    return new() { Height = _HEIGHT, Luminances = luminances, Colors = colors, Registers = registers };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source();
    var decoded = AtariHardInterlaceFile.ToRawImage(AtariHardInterlaceFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(AtariHardInterlaceFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(_HEIGHT));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The width is fixed at 320 and the height is whatever the file is long enough for, up to 240.
    var decoded = AtariHardInterlaceFile.ToRawImage(AtariHardInterlaceFile.FromRawImage(_Source().SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(AtariHardInterlaceFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(77));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureTallerThanTheDisplayIsBroughtToWhatItShows() {
    var decoded = AtariHardInterlaceFile.ToRawImage(AtariHardInterlaceFile.FromRawImage(_Source().SampleTo(320, 400)));

    Assert.That(decoded.Height, Is.EqualTo(AtariHardInterlaceFile.MaxHeight));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AtariHardInterlaceFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheRegistersAreWrittenAndTheFirstIsLeftBlack() {
    // A file that omits them is read as a plain luminance ramp, which is the one thing a picture
    // chosen for its colours cannot be.
    var bytes = AtariHardInterlaceWriter.ToBytes(AtariHardInterlaceFile.FromRawImage(_Source()));
    var fieldSize = _HEIGHT * AtariHardInterlaceFile.RowStride;

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(fieldSize * 2 + AtariHardInterlaceFile.RegisterBlockSize));
      Assert.That(bytes.Length % AtariHardInterlaceFile.PairStride, Is.EqualTo(AtariHardInterlaceFile.RegisterBlockSize));
      Assert.That(bytes[fieldSize * 2], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = AtariHardInterlaceFile.FromRawImage(_Source());
    var restored = AtariHardInterlaceReader.FromBytes(AtariHardInterlaceWriter.ToBytes(file));

    Assert.That(
      _Rgb(AtariHardInterlaceFile.ToRawImage(restored)), Is.EqualTo(_Rgb(AtariHardInterlaceFile.ToRawImage(file))));
  }
}
