using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes real On2 VP7 films here and in FFmpeg and requires every sample to match.</summary>
/// <remarks>
/// Opt-in: point <c>VP7_SAMPLES</c> at a copy of <c>samples.ffmpeg.org/V-codecs/VP7/</c> and the FATE
/// suite's <c>vp7/</c>. FFmpeg is asked for <c>yuv420p</c>, the codec's own layout.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class Vp7RealSampleOracleTests {

  [Test]
  [Explicit("Needs a directory of real VP7 films; point VP7_SAMPLES at one.")]
  public void EveryPictureOfARealVp7FilmMatchesFFmpeg()
    => RealSampleComparison.Run("VP7_SAMPLES", Vp7VideoDecoder.Accepts, "yuv420p");
}
