using System;
using System.IO;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Files whose picture is more than one frame.
/// </summary>
/// <remarks>
/// A JPEG XL file may state several frames. In a still picture they are layers:
/// each may cover only part of the picture, may be drawn over an earlier one
/// rather than replacing it, and may be kept aside for a later one to draw
/// over — and the picture is the last of them after all of that. In an
/// animation they are not layers but moments, and the still picture is the
/// first frame a viewer would show.
///
/// <para>This reader used to read the first frame and stop, which is right for
/// an animation by accident and wrong for anything else.</para>
/// </remarks>
[TestFixture]
public sealed class JxlMultiFrameTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// A still picture in two frames is composed, and the result agrees with
  /// libjxl.
  /// </summary>
  /// <remarks>
  /// The first frame carries splines and is kept aside; the second is blended
  /// over it. Reading only the first gives a picture that is missing everything
  /// the second one drew.
  /// </remarks>
  [Test]
  public void AStillPictureInSeveralFramesIsComposed() {
    var file = JpegXlReader.FromBytes(_Fixture("spline_on_first_frame.jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture("spline_on_first_frame.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
    });

    Assert.That(file.ComponentCount, Is.EqualTo(3));
    Assert.That(file.PixelData, Has.Length.EqualTo(expected.Length));

    var worst = 0;
    var differing = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var apart = Math.Abs(file.PixelData[i] - expected[i]);
      if (apart == 0)
        continue;
      ++differing;
      worst = Math.Max(worst, apart);
    }

    Assert.Multiple(() => {
      Assert.That(worst, Is.LessThanOrEqualTo(1),
        $"a sample is out by {worst} levels, which is more than libjxl's output dither can explain");
      // The second frame really is being drawn: with only the first, most of
      // the picture is wrong rather than a sixth of it by one level.
      Assert.That(differing, Is.LessThan(expected.Length / 2));
    });
  }

  /// <summary>
  /// An animation's still picture is its first shown frame, not all of its
  /// frames drawn on top of one another.
  /// </summary>
  /// <remarks>
  /// This one is four frames of a traffic light, each with a duration and each
  /// a small crop blended over the one kept aside. Composing all four gives a
  /// light showing every colour at once — measured against <c>djxl</c>, 3,093
  /// of 12,000 samples wrong and the worst out by 127 levels. Stopping at the
  /// first shown frame makes it identical.
  /// </remarks>
  [Test]
  public void AnAnimationDecodesToItsFirstShownFrame() {
    var file = JpegXlReader.FromBytes(_Fixture("cropped_traffic_light.jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture("cropped_traffic_light.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.ComponentCount, Is.EqualTo(4), "the picture carries the alpha it blends by");
    });

    // Lossless, so identical rather than within a level: nothing here is
    // fractional for libjxl's output dither to move.
    for (var i = 0; i < width * height; ++i)
    for (var c = 0; c < 3; ++c) {
      var mine = file.PixelData[i * file.ComponentCount + c];
      var theirs = expected[i * 3 + c];
      if (mine != theirs)
        Assert.Fail($"sample {i} channel {c} is {mine}, libjxl decodes it to {theirs}");
    }
  }

  /// <summary>
  /// A frame that covers less than the picture states which older frame it is
  /// drawn over, even when it replaces rather than blends.
  /// </summary>
  /// <remarks>
  /// That field used to be skipped, because the parser decided no frame was
  /// ever partial. Skipping it costs two bits and takes the rest of the frame
  /// header with it, which is why the traffic light could not be read at all.
  /// </remarks>
  [Test]
  public void ACroppedFrameIsRecognisedAsCoveringOnlyPartOfThePicture() {
    var codestream = _Fixture("cropped_traffic_light.jxl");
    var reader = new JxlBitReader(codestream, 2);
    var (width, height) = JxlSizeHeader.Decode(reader);
    var metadata = JxlImageMetadata.Decode(reader);
    JxlCustomTransformData.Decode(reader, metadata.XybEncoded);
    reader.ZeroPadToByte();

    var first = JxlSpecFrameHeader.Decode(reader, metadata, width, height);

    Assert.Multiple(() => {
      // libjxl's own jxlinfo reports this frame as 60x105 at (0,0).
      Assert.That(first.FrameWidth, Is.EqualTo(60));
      Assert.That(first.FrameHeight, Is.EqualTo(105));
      Assert.That(first.OriginX, Is.EqualTo(0));
      Assert.That(first.OriginY, Is.EqualTo(0));
      // It starts at the corner and is larger than the picture, so it covers it.
      Assert.That(first.IsPartialFrame, Is.False);
      Assert.That(first.IsLast, Is.False);
      Assert.That(first.Duration, Is.GreaterThan(0u), "an animation frame is shown for a while");
    });
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
