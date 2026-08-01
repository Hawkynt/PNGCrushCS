using System;
using System.Buffers.Binary;
using FileFormat.Cineon;
using FileFormat.Core;

namespace FileFormat.Cineon.Tests;

/// <summary>
/// Checks the printing-density scale a Cineon file's code values live on.
/// </summary>
/// <remarks>
/// A Cineon file holds density, not brightness, and the code values were being passed on as though
/// they were ordinary samples. That leaves an image with its blacks lifted and its whites pulled
/// down — the washed-out look of an unconverted film scan. It is invisible to a round trip, because
/// the writer left the same step out.
///
/// The pairs below were measured from files ImageMagick wrote: for each display value it was given,
/// the code value it chose.
/// </remarks>
[TestFixture]
public sealed class CineonTransferTests {

  /// <summary>Display value, and the code value ImageMagick writes for it.</summary>
  [Test]
  [Category("Unit")]
  [TestCase(0, 95)]    // reference black
  [TestCase(32, 204)]
  [TestCase(64, 322)]
  [TestCase(128, 490)]
  [TestCase(192, 602)]
  [TestCase(255, 684)] // one below reference white, which is 685
  public void Decode_CodeValue_GivesBackTheDisplayValueItStandsFor(int expected, int code) {
    var file = new CineonFile {
      Width = 1,
      Height = 1,
      BitsPerSample = 10,
      PixelData = _Packed(code),
    };

    var raw = CineonFile.ToRawImage(file);
    var red = raw.ToRgb24()[0];

    Assert.That(red, Is.EqualTo(expected).Within(1), $"code {code}");
  }

  /// <summary>
  /// Reference black is not zero exposure but the toe of the film, so its share has to come off
  /// before the rest is stretched over the range. Left in, black opens at about 26 rather than 0.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Decode_ReferenceBlack_IsActuallyBlack() {
    var file = new CineonFile { Width = 1, Height = 1, BitsPerSample = 10, PixelData = _Packed(95) };

    Assert.That(CineonFile.ToRawImage(file).ToRgb24()[0], Is.Zero);
  }

  [Test]
  [Category("Unit")]
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(64)]
  [TestCase(128)]
  [TestCase(200)]
  [TestCase(255)]
  public void EncodeThenDecode_ReturnsTheSameDisplayValue(byte value) {
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [value, value, value],
    };

    var back = CineonFile.ToRawImage(CineonFile.FromRawImage(source)).ToRgb24();

    Assert.That(back[0], Is.EqualTo(value).Within(1));
  }

  /// <summary>
  /// Each colour needs its own element record, and <c>NumElements</c> has to say so. Describing one
  /// channel while writing three made ImageMagick read the file as a single channel.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Write_DescribesAllThreeChannels() {
    var source = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[2 * 2 * 3],
    };

    var bytes = CineonWriter.ToBytes(CineonFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(bytes[193], Is.EqualTo(3), "num_elements");
      Assert.That(bytes[198], Is.EqualTo(10), "red bits per sample");
      Assert.That(bytes[226], Is.EqualTo(10), "green bits per sample");
      Assert.That(bytes[254], Is.EqualTo(10), "blue bits per sample");
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(228)), Is.EqualTo(2), "green pixels per line");
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(256)), Is.EqualTo(2), "blue pixels per line");
    });
  }

  /// <summary>Packs one 10-bit code into all three channels of a Cineon word.</summary>
  private static byte[] _Packed(int code) {
    var word = ((uint)code << 22) | ((uint)code << 12) | ((uint)code << 2);
    var bytes = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, word);
    return bytes;
  }
}
