using System;
using System.IO;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Where a picture's embedded colour profile sits in the codestream.
/// </summary>
/// <remarks>
/// A picture may carry an ICC profile, and it is entropy-coded: every byte is
/// coded in a context built from the two before it, so there is no length to
/// step over. The only way past it is to decode it, and the frames start where
/// it ends.
///
/// <para>It follows the custom-transform bundle rather than the image metadata,
/// which is one bundle later than this reader used to look. libjxl's own
/// decoder names a field for that ordering — <c>got_transform_data</c>, with
/// the comment "to skip everything before ICC". Reading the profile a bundle
/// early meant a file that carried one could not be opened at all: the read
/// landed in the middle of the transform data and failed on a histogram that
/// was not one.</para>
/// </remarks>
[TestFixture]
public sealed class JxlIccProfileOrderTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// A picture carrying a colour profile decodes, and to exactly what libjxl
  /// decodes it to.
  /// </summary>
  /// <remarks>
  /// Identical rather than within a level: the file is lossless, so nothing in
  /// it is fractional for <c>djxl</c>'s output dither to move.
  /// </remarks>
  [Test]
  public void APictureWithAColourProfileDecodesToWhatLibjxlDecodesItTo() {
    var file = JpegXlReader.FromBytes(_Fixture("relossless_8x8.jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture("relossless_8x8.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.ComponentCount, Is.EqualTo(3));
    });

    Assert.That(file.PixelData, Has.Length.EqualTo(expected.Length));
    for (var i = 0; i < expected.Length; ++i)
      if (file.PixelData[i] != expected[i])
        Assert.Fail($"sample {i} is {file.PixelData[i]}, libjxl decodes it to {expected[i]}");
  }

  /// <summary>
  /// The profile is not part of the image metadata bundle: reading it there
  /// consumes the transform data instead and the whole file is lost.
  /// </summary>
  [Test]
  public void TheProfileFollowsTheTransformBundleRatherThanTheMetadata() {
    var codestream = _Fixture("relossless_8x8.jxl");

    var reader = new JxlBitReader(codestream, 2);
    JxlSizeHeader.Decode(reader);
    var metadata = JxlImageMetadata.Decode(reader);
    Assert.That(metadata.ColorEncoding.WantIcc, Is.True, "the fixture is meant to carry a profile");

    // Reading the metadata leaves the reader on the transform bundle, not on
    // the profile. Trying to read a profile here is what used to happen, and it
    // finds no sensible entropy stream.
    var tooEarly = new JxlBitReader(codestream, 2);
    JxlSizeHeader.Decode(tooEarly);
    JxlImageMetadata.Decode(tooEarly);
    Assert.That(() => JxlIccProfileDecoder.Read(tooEarly), Throws.InstanceOf<Exception>());

    // In the right place it decodes, and to a real profile: an ICC file states
    // its own length in its first four bytes, big-endian.
    JxlCustomTransformData.Decode(reader, metadata.XybEncoded);
    var profile = JxlIccProfileDecoder.Read(reader);
    Assert.That(profile, Is.Not.Empty);
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(byte[] ppm) {
    var at = 0;
    string Token() {
      while (at < ppm.Length && char.IsWhiteSpace((char)ppm[at]))
        ++at;
      var start = at;
      while (at < ppm.Length && !char.IsWhiteSpace((char)ppm[at]))
        ++at;
      return System.Text.Encoding.ASCII.GetString(ppm, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"));
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at;

    var pixels = new byte[width * height * 3];
    Array.Copy(ppm, at, pixels, 0, pixels.Length);
    return (width, height, pixels);
  }
}
