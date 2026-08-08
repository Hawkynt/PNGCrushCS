using System;
using FileFormat.Core;

namespace FileFormat.Apac3.Tests;

[TestFixture]
public sealed class Apac3FileFromRawImageTests {

  /// <summary>
  /// A picture built by decoding a file, since only a file can say what the format holds: a hue row
  /// has no luminance of its own and takes the mean of its neighbours', so what a scanline shows
  /// depends on the rows either side of it.
  /// </summary>
  /// <remarks>
  /// The luminance changes across the picture and the hue down it. A luminance that changed down it
  /// would be smeared into its neighbours, and the mean of two luminances is a shade the format
  /// holds but not one the picture would still be a statement of.
  /// </remarks>
  private static Apac3File _Handmade() {
    var data = new byte[Apac3File.CompactSize];

    for (var row = 0; row < Apac3File.SourceRows; ++row) {
      var hue = 1 + (row / 4) % 15;

      for (var nibble = 0; nibble < Apac3File.Width / 4; ++nibble) {
        var luminance = (nibble / 5) & 15;
        var shift = (nibble & 1) == 0 ? 4 : 0;
        var at = row * Apac3File.RowStride + (nibble >> 1);
        data[at] |= (byte)(luminance << shift);
        data[at + Apac3File.FieldStride] |= (byte)(luminance << shift);
        data[Apac3File.CompactHueOffset + at] |= (byte)(hue << shift);
        data[Apac3File.CompactHueOffset + at + Apac3File.FieldStride] |= (byte)(hue << shift);
      }
    }

    return new() { Data = data, HueOffset = Apac3File.CompactHueOffset };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesWhatTheFormatShowsOfAPictureItHolds() {
    // Not to the byte, and the reason is the format rather than the encoder: the first stored row
    // shows one field's luminance against the other's halved, because a hue row averages the
    // luminances above and below it and above the first row there is nothing. Four streams reach
    // every scanline and settling them is a search, so what is asserted is that the picture comes
    // back recognisably itself — a field written where another belongs misses by far more.
    var source = Apac3File.ToRawImage(_Handmade());
    var decoded = Apac3File.ToRawImage(Apac3File.FromRawImage(source));

    var expected = _Rgb(source);
    var actual = _Rgb(decoded);
    long total = 0;
    for (var i = 0; i < expected.Length; ++i)
      total += Math.Abs(expected[i] - actual[i]);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(Apac3File.Width));
      Assert.That(decoded.Height, Is.EqualTo(Apac3File.Height));
      Assert.That(total / (double)expected.Length, Is.LessThan(1.0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = Apac3File.ToRawImage(
      Apac3File.FromRawImage(Apac3File.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(Apac3File.Width));
      Assert.That(decoded.Height, Is.EqualTo(Apac3File.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => Apac3File.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheLengthIsTheWholeHeaderAndTheFourHalvesInterleaveByRow() {
    // Nothing in the file says where the hues begin except how long it is, and within each half the
    // second field's row sits between the first field's rows.
    var bytes = Apac3Writer.ToBytes(Apac3File.FromRawImage(Apac3File.ToRawImage(_Handmade())));
    var restored = Apac3Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(Apac3File.CompactSize));
      Assert.That(restored.HueOffset, Is.EqualTo(Apac3File.CompactHueOffset));
      Assert.That(Apac3File.RowStride, Is.EqualTo(Apac3File.FieldStride * 2));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = Apac3File.FromRawImage(Apac3File.ToRawImage(_Handmade()));
    var restored = Apac3Reader.FromBytes(Apac3Writer.ToBytes(file));

    Assert.That(_Rgb(Apac3File.ToRawImage(restored)), Is.EqualTo(_Rgb(Apac3File.ToRawImage(file))));
  }
}
