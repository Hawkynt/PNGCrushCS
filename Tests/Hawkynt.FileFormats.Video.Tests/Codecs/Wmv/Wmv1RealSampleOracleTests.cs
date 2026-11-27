using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes real and FFmpeg-written Windows Media Video 7 films here and in FFmpeg and requires every sample to match.</summary>
/// <remarks>
/// Opt-in: point <c>WMV1_SAMPLES</c> at a directory holding <c>samples.ffmpeg.org/V-codecs/WMV1/Damien.wmv</c>
/// and whatever else is to be compared. FFmpeg is asked for <c>yuv420p</c>, the codec's own layout.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class Wmv1RealSampleOracleTests {

  [Test]
  [Explicit("Needs a directory of WMV1 films; point WMV1_SAMPLES at one.")]
  public void EveryPictureOfAWmv1FilmMatchesFFmpeg()
    => RealSampleComparison.Run("WMV1_SAMPLES", Wmv1VideoDecoder.Accepts, "yuv420p");
}
