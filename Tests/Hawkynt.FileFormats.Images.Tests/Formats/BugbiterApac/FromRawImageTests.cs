using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.BugbiterApac.Tests;

[TestFixture]
public sealed class BugbiterApacFileFromRawImageTests {

  /// <summary>
  /// A picture built by decoding a file, since only a file can say what the format holds: a hue row
  /// has no luminance of its own and takes the mean of its neighbours', so what a scanline shows
  /// depends on the rows either side of it.
  /// </summary>
  private static BugbiterApacFile _Handmade() {
    var data = new byte[BugbiterApacFile.BaseFileSize];
    Encoding.ASCII.GetBytes(BugbiterApacFile.Signature).CopyTo(data, 0);
    data[30] = 255;
    data[31] = 80;
    data[32] = BugbiterApacFile.Height;

    var picture = BugbiterApacFile.TextOffset;
    BugbiterApacFile.HalfMarker.CopyTo(data.AsSpan(picture));
    BugbiterApacFile.HalfMarker.CopyTo(data.AsSpan(picture + BugbiterApacFile.SecondHueOffset - 2));

    for (var row = 0; row < BugbiterApacFile.LongRows; ++row) {
      var hue = 1 + (row / 5) % 15;

      for (var nibble = 0; nibble < BugbiterApacFile.Width / 4; ++nibble) {
        var luminance = (nibble / 7) & 15;
        var shift = (nibble & 1) == 0 ? 4 : 0;
        var at = row * BugbiterApacFile.Stride + (nibble >> 1);

        data[picture + BugbiterApacFile.FirstLuminanceOffset + at] |= (byte)(luminance << shift);
        data[picture + BugbiterApacFile.SecondHueOffset + at] |= (byte)(hue << shift);

        if (row >= BugbiterApacFile.ShortRows)
          continue;

        data[picture + BugbiterApacFile.SecondLuminanceOffset + at] |= (byte)(luminance << shift);
        data[picture + BugbiterApacFile.FirstHueOffset + at] |= (byte)(hue << shift);
      }
    }

    return new() { Data = data, PictureOffset = picture };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesWhatTheFormatShowsOfAPictureItHolds() {
    // Not to the byte, and the reason is the format rather than the encoder: the first stored row
    // shows one field's luminance against the other's halved, because a hue row averages the
    // luminances above and below it and above the first row there is nothing. What is asserted is
    // that the picture comes back recognisably itself — a field written where another belongs
    // misses by far more.
    var source = BugbiterApacFile.ToRawImage(_Handmade());
    var decoded = BugbiterApacFile.ToRawImage(BugbiterApacFile.FromRawImage(source));

    var expected = _Rgb(source);
    var actual = _Rgb(decoded);
    long total = 0;
    for (var i = 0; i < expected.Length; ++i)
      total += Math.Abs(expected[i] - actual[i]);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(BugbiterApacFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(BugbiterApacFile.Height));
      Assert.That(total / (double)expected.Length, Is.LessThan(1.0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = BugbiterApacFile.ToRawImage(
      BugbiterApacFile.FromRawImage(BugbiterApacFile.ToRawImage(_Handmade()).SampleTo(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(BugbiterApacFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(BugbiterApacFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => BugbiterApacFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheOddRowCountFallsUnevenlyBetweenTheFieldsAndTheHalvesAreMarked() {
    // 239 does not halve, so one field's luminances cover one row more than the other's; and the two
    // bytes each half opens with are the only check the reader has that the comment's length pointed
    // at the picture rather than into it.
    var bytes = BugbiterApacWriter.ToBytes(BugbiterApacFile.FromRawImage(BugbiterApacFile.ToRawImage(_Handmade())));
    var picture = BugbiterApacFile.TextOffset;

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(BugbiterApacFile.BaseFileSize));
      Assert.That(bytes[BugbiterApacFile.TextLengthOffset], Is.EqualTo(0));
      Assert.That(bytes[BugbiterApacFile.TextLengthOffset + 1], Is.EqualTo(0));
      Assert.That(bytes[picture], Is.EqualTo(88));
      Assert.That(bytes[picture + 1], Is.EqualTo(37));
      Assert.That(bytes[picture + BugbiterApacFile.SecondHueOffset - 2], Is.EqualTo(88));
      Assert.That(bytes[picture + BugbiterApacFile.SecondHueOffset - 1], Is.EqualTo(37));
      Assert.That(BugbiterApacFile.LongRows - BugbiterApacFile.ShortRows, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = BugbiterApacFile.FromRawImage(BugbiterApacFile.ToRawImage(_Handmade()));
    var restored = BugbiterApacReader.FromBytes(BugbiterApacWriter.ToBytes(file));

    Assert.That(
      _Rgb(BugbiterApacFile.ToRawImage(restored)), Is.EqualTo(_Rgb(BugbiterApacFile.ToRawImage(file))));
  }
}
