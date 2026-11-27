using System;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes real Sorenson Video 1 films here and in FFmpeg and requires every sample to match.</summary>
/// <remarks>
/// Opt-in: point <c>SVQ1_SAMPLES</c> at a copy of <c>samples.ffmpeg.org/V-codecs/SVQ1/</c> and the FATE
/// suite's <c>svq1/</c>. FFmpeg is asked for <c>yuv410p</c>, the codec's own layout.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class Svq1RealSampleOracleTests {

  [Test]
  [Explicit("Needs a directory of real SVQ1 films; point SVQ1_SAMPLES at one.")]
  public void EveryPictureOfARealSvq1FilmMatchesFFmpeg()
    => RealSampleComparison.Run("SVQ1_SAMPLES", Svq1VideoDecoder.Accepts, "yuv410p", _FirstCodedDifference);

  /// <summary>
  /// Compares what SVQ1 codes. Its chrominance planes are coded at a quarter of the picture size rounded
  /// down to whole 16x16 blocks of that, so a picture whose height or width is not a multiple of four,
  /// with the quarter a multiple of sixteen, has a last chrominance row or column no decoder writes —
  /// FFmpeg hands out whatever its buffer held there and this decoder repeats the edge.
  /// </summary>
  private static int _FirstCodedDifference(RawImage ours, ReadOnlyMemory<byte> theirs) {
    var a = ours.PixelData;
    var b = theirs.Span;
    var lumaLength = ours.Width * ours.Height;
    for (var i = 0; i < lumaLength; ++i)
      if (a[i] != b[i])
        return i;

    var chromaWidth = (ours.Width + 3) >> 2;
    var chromaHeight = (ours.Height + 3) >> 2;
    var codedWidth = Math.Min(chromaWidth, ((ours.Width / 4) + 15) & ~15);
    var codedHeight = Math.Min(chromaHeight, ((ours.Height / 4) + 15) & ~15);
    for (var plane = 0; plane < 2; ++plane)
    for (var y = 0; y < codedHeight; ++y)
    for (var x = 0; x < codedWidth; ++x) {
      var at = lumaLength + plane * chromaWidth * chromaHeight + y * chromaWidth + x;
      if (a[at] != b[at])
        return at;
    }
    return -1;
  }
}
