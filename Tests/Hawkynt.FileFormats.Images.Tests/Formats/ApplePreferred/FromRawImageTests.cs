using System;
using FileFormat.Core;

namespace FileFormat.ApplePreferred.Tests;

[TestFixture]
public sealed class ApplePreferredFileFromRawImageTests {

  /// <summary>Sixteen colours on the four-bit grid a IIGS palette stores them in.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x / 2 + y) & 15;
      var at = (y * width + x) * 3;
      rgb[at] = ChannelScaling.Expand4(index);
      rgb[at + 1] = ChannelScaling.Expand4(15 - index);
      rgb[at + 2] = ChannelScaling.Expand4((index * 5) & 15);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    // Thirty-four across is two pixels a byte and not a whole multiple of eight, so a scanline whose
    // packed length was miscounted would drag every following line out of step.
    var source = _Source(34, 11);
    var decoded = ApplePreferredFile.ToRawImage(ApplePreferredFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(34));
      Assert.That(decoded.Height, Is.EqualTo(11));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnOddWidthIsMovedRatherThanRefused() {
    // A byte holds two pixels and half a byte cannot be stored, so an odd width is the one size the
    // format has to move; everything else is kept as it stands.
    var decoded = ApplePreferredFile.ToRawImage(ApplePreferredFile.FromRawImage(_Source(35, 11)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(34));
      Assert.That(decoded.Height, Is.EqualTo(11));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => ApplePreferredFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ASmallPictureIsPaddedUpToTheLengthTheReaderInsistsOn() {
    // Anything under 1249 bytes is not recognised at all, and the chunk's own length covers the
    // padding so that the chunk walk still ends where the file does.
    var bytes = ApplePreferredWriter.ToBytes(ApplePreferredFile.FromRawImage(_Source(34, 11)));
    var chunk = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(ApplePreferredFile.MinimumFileSize));
      Assert.That(chunk, Is.EqualTo(bytes.Length));
      Assert.That(bytes[13], Is.EqualTo(1));
      Assert.That(bytes[9] & 240, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = ApplePreferredFile.FromRawImage(_Source(34, 11));
    var restored = ApplePreferredReader.FromBytes(ApplePreferredWriter.ToBytes(file));

    Assert.That(
      _Rgb(ApplePreferredFile.ToRawImage(restored)), Is.EqualTo(_Rgb(ApplePreferredFile.ToRawImage(file))));
  }
}
