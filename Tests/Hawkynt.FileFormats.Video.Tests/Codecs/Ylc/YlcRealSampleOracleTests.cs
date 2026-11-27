using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes real YLC films here and in FFmpeg and requires every sample to match.</summary>
/// <remarks>Opt-in: point <c>YLC_SAMPLES</c> at a directory holding <c>samples.ffmpeg.org/V-codecs/ylc0.avi</c>.</remarks>
[TestFixture]
[Category("Conformance")]
public sealed class YlcRealSampleOracleTests {

  [Test]
  [Explicit("Needs a directory of real YLC films; point YLC_SAMPLES at one.")]
  public void EveryPictureOfARealYlcFilmMatchesFFmpeg()
    => RealSampleComparison.Run("YLC_SAMPLES", YlcVideoDecoder.Accepts, "yuv422p");
}
