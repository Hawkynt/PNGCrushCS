using System;
using FileFormat.Core;

namespace FileFormat.GedPicture.Tests;

[TestFixture]
public sealed class GedPictureFileFromRawImageTests {

  /// <summary>The timing the handmade picture was drawn against, which the encoder has to find.</summary>
  private const byte _CYCLE = 5;

  /// <summary>
  /// A picture built as a file, since the six colour rewrites land where the timing puts them and
  /// not where a drawing would want them.
  /// </summary>
  /// <remarks>
  /// Nine colours to a scanline, all of different hue, and a playfield that uses all four registers
  /// of every segment — so nothing the encoder recovers can be recovered by accident.
  /// </remarks>
  private static GedPictureFile _Handmade() {
    var data = new byte[GedPictureFile.FileSize];
    GedPictureFile.Signature.CopyTo(data);
    data[3292] = 1;
    data[3300] = _CYCLE;

    ReadOnlySpan<byte> hues = [0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0, 0xE0, 0x30];
    ReadOnlySpan<byte> steps = [3, 5, 7, 9, 11, 13, 15, 17];

    for (var y = 0; y < GedPictureFile.Height; ++y) {
      data[GedPictureFile.PokeAddressOffset + y] = 26;
      data[GedPictureFile.PokeValueOffset + y] = (byte)(0x50 | (y * 2 & 14));

      for (var table = 0; table < GedPictureEncoder.TableCount; ++table)
        data[GedPictureFile.ColorTablesOffset + table * GedPictureFile.Height + y]
          = (byte)(hues[table] | (y * steps[table] & 14));

      for (var column = 0; column < GedPictureFile.Columns; ++column)
        data[GedPictureFile.PlayfieldOffset + y * GedPictureFile.Columns + column]
          = (byte)(y * 37 + column * 91 + (y >> 2) * 13);
    }

    return GedPictureReader.FromBytes(data);
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = GedPictureFile.ToRawImage(_Handmade());
    var decoded = GedPictureFile.ToRawImage(GedPictureFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(GedPictureFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(GedPictureFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTimingThePictureWasDrawnAgainstIsFoundAgain() {
    // The six rewrites land where the timing puts them, so a picture read back against another
    // timing has its colours in the wrong places. Nothing in the picture says which timing it was;
    // it has to be recognised from how well the colours fit.
    var file = GedPictureFile.FromRawImage(GedPictureFile.ToRawImage(_Handmade()));

    Assert.That(file.Cycle, Is.EqualTo(_CYCLE));
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = GedPictureFile.ToRawImage(
      GedPictureFile.FromRawImage(GedPictureFile.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(GedPictureFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(GedPictureFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheOneFreeRegisterWriteAScanlineGetsIsSpentOnTheBackground() {
    // The quirk: the background is one register for the whole picture, which would leave a scanline
    // with three colours rather than four. The free write a scanline may make is a way round it, and
    // the only thing it is worth spending on when there are no sprites to move.
    var bytes = GedPictureWriter.ToBytes(GedPictureFile.FromRawImage(GedPictureFile.ToRawImage(_Handmade())));

    for (var y = 0; y < GedPictureFile.Height; ++y)
      Assert.That(bytes[GedPictureFile.PokeAddressOffset + y], Is.EqualTo(26), $"scanline {y}");
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = GedPictureFile.FromRawImage(GedPictureFile.ToRawImage(_Handmade()));
    var bytes = GedPictureWriter.ToBytes(file);
    var restored = GedPictureReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(GedPictureFile.FileSize));
      Assert.That(_Rgb(GedPictureFile.ToRawImage(restored)), Is.EqualTo(_Rgb(GedPictureFile.ToRawImage(file))));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => GedPictureFile.FromRawImage(null!));
}
