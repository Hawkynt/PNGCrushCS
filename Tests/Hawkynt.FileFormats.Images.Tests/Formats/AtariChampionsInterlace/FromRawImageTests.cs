using System;
using FileFormat.Core;

namespace FileFormat.AtariChampionsInterlace.Tests;

[TestFixture]
public sealed class AtariChampionsInterlaceFileFromRawImageTests {

  /// <summary>
  /// A picture built by decoding a file, since only a file can say what the format holds: a hue row
  /// averages the luminances above and below it, so what a scanline shows depends on its neighbours.
  /// </summary>
  private static AtariChampionsInterlaceFile _Handmade() {
    var data = new byte[AtariChampionsInterlaceFile.PerRowSize];

    for (var y = 0; y < AtariChampionsInterlaceFile.PerRowHeight; ++y) {
      // Four luminances a scanline, which is what the per-scanline registers are for.
      for (var slot = 0; slot < Atari8BitGraphics.Gr15RegisterCount; ++slot)
        data[AtariChampionsInterlaceFile.BareSize + y + slot * AtariChampionsInterlaceFile.RegisterPlane] =
          (byte)(slot * 4);

      for (var pixel = 0; pixel < AtariChampionsInterlaceFile.LogicalWidth; ++pixel)
        data[y * AtariChampionsInterlaceFile.Stride + (pixel >> 2)] |=
          (byte)(((pixel / 9) & 3) << ((3 - (pixel & 3)) << 1));
    }

    var hues = AtariChampionsInterlaceFile.Stride * AtariChampionsInterlaceFile.PerRowHeight;
    for (var row = 0; row < AtariChampionsInterlaceFile.PerRowHeight / 2; ++row)
    for (var nibble = 0; nibble < AtariChampionsInterlaceFile.HueNibbles; ++nibble) {
      var shift = (nibble & 1) == 0 ? 4 : 0;
      var hue = 1 + (row / 6) % 15;
      var at = hues + row * AtariChampionsInterlaceFile.HueStride + (nibble >> 1);
      data[at] |= (byte)(hue << shift);
      data[at + AtariChampionsInterlaceFile.Stride] |= (byte)(hue << shift);
    }

    return new() { Data = data, Height = AtariChampionsInterlaceFile.PerRowHeight };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesWhatTheFormatShowsOfAPictureItHolds() {
    // Not to the byte. A register's hue only reaches the screen on the very first scanline, where
    // there is no hue row above to overwrite it, and four streams reach every other one — so what is
    // asserted is that the picture comes back recognisably itself, which a field or a plane written
    // where another belongs misses by far more.
    var source = AtariChampionsInterlaceFile.ToRawImage(_Handmade());
    var decoded = AtariChampionsInterlaceFile.ToRawImage(AtariChampionsInterlaceFile.FromRawImage(source));

    var expected = _Rgb(source);
    var actual = _Rgb(decoded);
    long total = 0;
    for (var i = 0; i < expected.Length; ++i)
      total += Math.Abs(expected[i] - actual[i]);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(AtariChampionsInterlaceFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(AtariChampionsInterlaceFile.PerRowHeight));
      Assert.That(total / (double)expected.Length, Is.LessThan(2.0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = AtariChampionsInterlaceFile.ToRawImage(
      AtariChampionsInterlaceFile.FromRawImage(
        AtariChampionsInterlaceFile.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(AtariChampionsInterlaceFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(AtariChampionsInterlaceFile.PerRowHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AtariChampionsInterlaceFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void EveryScanlineGetsItsOwnRegistersAndTheyAreStoredAsPlanes() {
    // One register's values run contiguously down the screen, which is the order a display routine
    // rewriting one register per line wants to read them in — not four bytes per scanline.
    var bytes = AtariChampionsInterlaceWriter.ToBytes(
      AtariChampionsInterlaceFile.FromRawImage(AtariChampionsInterlaceFile.ToRawImage(_Handmade())));

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(AtariChampionsInterlaceFile.PerRowSize));

      // A register never contributes a hue, so nothing above the low four bits is ever written.
      for (var y = 0; y < AtariChampionsInterlaceFile.PerRowHeight; ++y)
      for (var slot = 0; slot < Atari8BitGraphics.Gr15RegisterCount; ++slot) {
        var at = AtariChampionsInterlaceFile.BareSize + y + slot * AtariChampionsInterlaceFile.RegisterPlane;
        Assert.That(bytes[at], Is.LessThan(16), $"row {y} register {slot}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = AtariChampionsInterlaceFile.FromRawImage(AtariChampionsInterlaceFile.ToRawImage(_Handmade()));
    var restored = AtariChampionsInterlaceReader.FromBytes(AtariChampionsInterlaceWriter.ToBytes(file));

    Assert.That(
      _Rgb(AtariChampionsInterlaceFile.ToRawImage(restored)),
      Is.EqualTo(_Rgb(AtariChampionsInterlaceFile.ToRawImage(file))));
  }
}
