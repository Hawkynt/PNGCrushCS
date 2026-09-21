using System;
using System.Buffers.Binary;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// Compares this package's decoded component planes against FFmpeg's raw planes for the same frame.
/// </summary>
/// <remarks>
/// <b>Why component planes and not pixels.</b> Everything after the planes — narrowing to eight bits,
/// choosing a colour matrix, resampling chroma across every luma column — is a display convention two
/// correct decoders are free to disagree about. Comparing packed colour measures the conventions
/// loudly enough to drown a real defect.
/// <para/>
/// <b>Why one level of colour and none of alpha.</b> RDD 36's inverse transform is specified as an
/// exact integer process, but the rounding of the two ×(1/√2) butterflies is where independent
/// implementations of it differ, and the difference reaches at most the last coded level. Alpha has
/// no transform and no quantiser at all: it is a run-length code over the samples themselves, so a
/// single differing alpha sample is a defect and not a rounding.
/// </remarks>
internal static class ProResPlaneComparison {

  /// <summary>Requires FFmpeg's three colour planes to match the decoded ones within a tolerance.</summary>
  internal static void AssertColour(
    byte[] decoded,
    ProResPlanes expected,
    int width,
    int height,
    int chromaShift,
    int maximumDelta) {
    var chromaWidth = (width + (1 << chromaShift) - 1) >> chromaShift;
    var lumaSamples = checked(width * height);
    var chromaSamples = checked(chromaWidth * height);
    var required = checked((lumaSamples + chromaSamples * 2) * 2);
    Assert.That(decoded.Length, Is.GreaterThanOrEqualTo(required),
      "ffmpeg returned fewer bytes than three planes of this geometry need");

    _AssertPlane(decoded, 0, expected.Luma, expected.Width, width, height, maximumDelta, "Y");
    _AssertPlane(
      decoded, lumaSamples * 2, expected.Cb, expected.ChromaWidth, chromaWidth, height, maximumDelta, "Cb");
    _AssertPlane(
      decoded, (lumaSamples + chromaSamples) * 2, expected.Cr, expected.ChromaWidth, chromaWidth, height,
      maximumDelta, "Cr");
  }

  /// <summary>
  /// Requires FFmpeg's colour planes to still be the picture the encoder was handed.
  /// </summary>
  /// <remarks>
  /// <b>Why this is not the same check as <see cref="AssertColour"/>.</b> That one compares two
  /// decoders over one bitstream, and it is blind by construction to a writer which produced a
  /// perfectly well-formed frame of the wrong picture — both decoders then read the wrong picture
  /// correctly and agree about it completely. An encoder that wrote a field picture with the
  /// progressive coefficient scan is exactly that: RDD 36 says a field picture is read with the other
  /// scan, every conforming decoder obligingly does so, and they all produce the same scrambled
  /// detail. Only the source says otherwise.
  /// <para/>
  /// The tolerance is per profile, because this is the lossy direction: ProRes quantises, and how far
  /// it is allowed to move a sample is the one thing the profile actually decides. The figures are
  /// measured over a band-limited picture at each profile's own data rate with headroom above the
  /// worst sample, and they stay far below the hundreds of levels any scan, block-order or field
  /// mapping defect costs.
  /// </remarks>
  internal static void AssertColourResemblesSource(
    byte[] decoded,
    ushort[][] source,
    int width,
    int height,
    int chromaShift,
    int maximumDelta) {
    var chromaWidth = (width + (1 << chromaShift) - 1) >> chromaShift;
    var lumaSamples = checked(width * height);
    var chromaSamples = checked(chromaWidth * height);

    _AssertPlane(decoded, 0, source[0], width, width, height, maximumDelta, "source Y");
    _AssertPlane(
      decoded, lumaSamples * 2, source[1], chromaWidth, chromaWidth, height, maximumDelta, "source Cb");
    _AssertPlane(
      decoded, (lumaSamples + chromaSamples) * 2, source[2], chromaWidth, chromaWidth, height,
      maximumDelta, "source Cr");
  }

  /// <summary>
  /// Requires FFmpeg's alpha plane to be the matte that was handed to the encoder, sample for sample.
  /// </summary>
  /// <remarks>
  /// <b>The matte, not this package's decode of it.</b> Comparing the two decoders cannot see an
  /// encoder which lost the matte before coding it: an encoder that wrote full opacity instead of the
  /// picture's alpha has written a perfectly valid frame, and both decoders read that frame back
  /// correctly and agree with each other completely. Only the source can tell them they are agreeing
  /// about the wrong picture — which is why the alpha assertions start from the matte and not from
  /// <see cref="ProResPlanes.Alpha"/>, and why they are exact. ProRes alpha is run-length coded, not
  /// transformed and not quantised, so there is no lossy step between the two to allow for.
  /// <para/>
  /// FFmpeg reconstructs 4444 at twelve bits and widens the coded alpha to it: an eight-bit sample
  /// becomes <c>(a &lt;&lt; 4) | (a &gt;&gt; 4)</c>, which maps 0 to 0 and 255 to 4095, and a
  /// sixteen-bit one becomes <c>a &gt;&gt; 4</c>.
  /// </remarks>
  /// <param name="decoded">FFmpeg's <c>yuva444p12le</c> planes.</param>
  /// <param name="matte">The alpha samples the encoder was given, at their coded depth.</param>
  /// <param name="width">The picture width.</param>
  /// <param name="height">The picture height.</param>
  /// <param name="alphaBitDepth">8 or 16, the depth the matte's samples are stated at.</param>
  internal static void AssertAlphaIsTheSourceMatte(
    byte[] decoded,
    ushort[] matte,
    int width,
    int height,
    int alphaBitDepth) {
    var planeSamples = checked(width * height);
    Assert.That(matte, Has.Length.EqualTo(planeSamples));

    var alphaOffset = checked(planeSamples * 3 * 2);
    Assert.That(decoded.Length, Is.EqualTo(alphaOffset + planeSamples * 2),
      "yuva444p12le should contain four equally sized twelve-bit planes");

    _AssertAlphaPlane(decoded, alphaOffset, matte, width, width, height, alphaBitDepth);
  }

  /// <summary>
  /// Requires FFmpeg's alpha plane to match this package's decode of the same FFmpeg-written frame.
  /// </summary>
  /// <remarks>
  /// The read direction, where the frame came from FFmpeg and neither side of it is ours, so the two
  /// decodes are independent and comparing them is evidence.
  /// <para/>
  /// <b>Exact, including the samples the encoder never wrote.</b> FFmpeg's ProRes 4444 encoder, up to
  /// and including 6.1, stops one sample short of every alpha slice — see
  /// <see cref="ProResPlanes.TruncatedAlphaSamples"/>. Neither decoder can produce the matte for
  /// those, because the matte is not in the file; both read zeroes past the end of the coded data and
  /// arrive at the same value, so there is still nothing to allow for here.
  /// </remarks>
  internal static void AssertAlphaAgreesWithFFmpeg(
    byte[] decoded,
    ProResPlanes planes,
    int width,
    int height,
    int alphaBitDepth) {
    Assert.That(planes.Alpha, Is.Not.Null, "the frame was expected to carry an alpha channel");
    Assert.That(planes.AlphaBitDepth, Is.EqualTo(alphaBitDepth));

    var planeSamples = checked(width * height);
    var alphaOffset = checked(planeSamples * 3 * 2);
    Assert.That(decoded.Length, Is.EqualTo(alphaOffset + planeSamples * 2),
      "yuva444p12le should contain four equally sized twelve-bit planes");

    _AssertAlphaPlane(decoded, alphaOffset, planes.Alpha!, planes.Width, width, height, alphaBitDepth);
  }

  private static void _AssertAlphaPlane(
    byte[] decoded,
    int alphaOffset,
    ushort[] source,
    int stride,
    int width,
    int height,
    int alphaBitDepth) {
    var differing = 0;
    var worst = 0;
    var firstX = -1;
    var firstY = -1;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var coded = source[y * stride + x];
        var wanted = alphaBitDepth == 8 ? (coded << 4) | (coded >> 4) : coded >> 4;
        var actual = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(alphaOffset + (y * width + x) * 2));
        var delta = Math.Abs(actual - wanted);
        if (delta == 0)
          continue;

        if (differing++ == 0)
          (firstX, firstY) = (x, y);
        worst = Math.Max(worst, delta);
      }

    Assert.That(worst, Is.Zero,
      $"ffmpeg's alpha plane differs in {differing} of {width * height} samples, "
      + $"first at {firstX},{firstY}; worst delta {worst}");
  }

  private static void _AssertPlane(
    byte[] decoded,
    int decodedOffset,
    ushort[] expected,
    int expectedStride,
    int width,
    int height,
    int maximumDelta,
    string component) {
    var differing = 0;
    var worst = 0;
    var firstX = -1;
    var firstY = -1;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var actual = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(decodedOffset + (y * width + x) * 2));
        var wanted = expected[y * expectedStride + x];
        var delta = Math.Abs(actual - wanted);
        if (delta == 0)
          continue;

        if (differing++ == 0)
          (firstX, firstY) = (x, y);
        worst = Math.Max(worst, delta);
      }

    Assert.That(worst, Is.LessThanOrEqualTo(maximumDelta),
      $"ffmpeg's {component} plane differs in {differing} of {width * height} samples, "
      + $"first at {firstX},{firstY}; worst delta {worst}");
  }
}
