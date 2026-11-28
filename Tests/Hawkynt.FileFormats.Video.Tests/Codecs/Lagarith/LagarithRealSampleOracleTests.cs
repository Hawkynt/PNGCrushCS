using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes real Lagarith films here and in FFmpeg and requires every sample to match.</summary>
/// <remarks>
/// Opt-in: point <c>LAGARITH_SAMPLES</c> at a directory with subdirectories <c>rgb</c>, <c>rgba</c>,
/// <c>yuv422</c> and <c>yuv420</c>, holding the films of <c>samples.ffmpeg.org/V-codecs/lagarith/</c> and the
/// FATE suite's <c>lagarith/</c> by the layout FFmpeg decodes them to.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class LagarithRealSampleOracleTests {

  [TestCase("rgb", "rgb24")]
  [TestCase("rgba", "rgba")]
  [TestCase("yuv422", "yuv422p")]
  [TestCase("yuv420", "yuv420p")]
  [Explicit("Needs a directory of real Lagarith films; point LAGARITH_SAMPLES at one.")]
  public void EveryPictureOfARealLagarithFilmMatchesFFmpeg(string layout, string pixelFormat) {
    var root = System.Environment.GetEnvironmentVariable("LAGARITH_SAMPLES");
    if (string.IsNullOrWhiteSpace(root))
      Assert.Inconclusive("Set LAGARITH_SAMPLES to a directory of real Lagarith films.");
    System.Environment.SetEnvironmentVariable("LAGARITH_SAMPLES_" + layout, System.IO.Path.Combine(root!, layout));
    RealSampleComparison.Run("LAGARITH_SAMPLES_" + layout, LagarithVideoDecoder.Accepts, pixelFormat);
  }
}
