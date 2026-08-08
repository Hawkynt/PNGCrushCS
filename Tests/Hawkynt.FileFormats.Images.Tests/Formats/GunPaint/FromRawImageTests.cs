using System;
using FileFormat.Core;

namespace FileFormat.GunPaint.Tests;

[TestFixture]
public sealed class GunPaintFileFromRawImageTests {

  /// <summary>A file whose every area says something, so that the encoder has all of them to find.</summary>
  /// <remarks>
  /// Built as a file rather than as a picture, because a picture the format holds is not something
  /// that can be drawn freehand: the second field is displaced a pixel from the first, so what a
  /// pixel shows is tied to what its neighbour shows, and only a file can state a combination the
  /// display would actually have produced.
  /// </remarks>
  private static GunPaintFile _Handmade() {
    var data = new byte[GunPaintFile.FileSize];

    for (var y = 0; y < GunPaintFile.Height; ++y)
      data[GunPaintFile.BackgroundOffsetFor(y)] = (byte)(y / 25 % 16);

    for (var row = 0; row < GunPaintFile.Height / 8; ++row)
    for (var column = 0; column < GunPaintFile.Width / 8; ++column) {
      var cell = row * GunPaintFile.StrideColumns + column;
      data[GunPaintFile.ColorRamOffset + cell] = (byte)((row * 3 + column) % 16);

      for (var line = 0; line < 8; ++line) {
        var matrix = line * GunPaintFile.MatrixStride + cell;
        data[GunPaintFile.FirstMatrixOffset + matrix] = (byte)(((column + line) % 16 << 4) | (row * 5 + 1) % 16);
        data[GunPaintFile.SecondMatrixOffset + matrix] = (byte)(((row + line) % 16 << 4) | (column * 7 + 2) % 16);
        data[GunPaintFile.FirstBitmapOffset + (cell << 3) + line] = (byte)(0x1B * (column + line + 1));
        data[GunPaintFile.SecondBitmapOffset + (cell << 3) + line] = (byte)(0x27 * (row + line + 1));
      }
    }

    return GunPaintReader.FromBytes(data);
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  private static double _MeanError(byte[] expected, byte[] actual) {
    long total = 0;
    for (var at = 0; at < expected.Length; ++at)
      total += Math.Abs(expected[at] - actual[at]);

    return total / (double)expected.Length;
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ComesBackAsCloseAsTheDisplacementAllows() {
    // Not to the byte, and the format rather than the encoder forbids it: the second field sits a
    // pixel to the right of the first, so its two-pixel blocks fall between the first field's and no
    // pixel has a block to itself. Where neighbouring pixels disagree, one of them is served by a
    // block chosen for the other, and the display averages the two disagreements. What is asserted
    // is therefore closeness — a field written where the other belongs misses by very much more.
    var source = GunPaintFile.ToRawImage(_Handmade());
    var decoded = GunPaintFile.ToRawImage(GunPaintFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(GunPaintFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(GunPaintFile.Height));
      Assert.That(_MeanError(_Rgb(source), _Rgb(decoded)), Is.LessThan(1.0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = GunPaintFile.ToRawImage(
      GunPaintFile.FromRawImage(GunPaintFile.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(GunPaintFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(GunPaintFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheLastFourScanlinesShareOneBackground() {
    // The quirk: the background table is in three pieces, and the third is a single byte serving
    // whatever the first two did not reach. Four scanlines therefore cannot disagree about it, so a
    // picture asking them to comes back with all four settled the same way.
    var bytes = GunPaintWriter.ToBytes(GunPaintFile.FromRawImage(GunPaintFile.ToRawImage(_Handmade())));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(GunPaintFile.FileSize));
      Assert.That(GunPaintFile.BackgroundOffsetFor(196), Is.EqualTo(GunPaintFile.BackgroundOffsetFor(199)));
      Assert.That(bytes[GunPaintFile.BackgroundOffsetFor(199)], Is.EqualTo(bytes[GunPaintFile.BackgroundOffsetFor(196)]));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = GunPaintFile.FromRawImage(GunPaintFile.ToRawImage(_Handmade()));
    var restored = GunPaintReader.FromBytes(GunPaintWriter.ToBytes(file));

    Assert.That(_Rgb(GunPaintFile.ToRawImage(restored)), Is.EqualTo(_Rgb(GunPaintFile.ToRawImage(file))));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => GunPaintFile.FromRawImage(null!));
}
