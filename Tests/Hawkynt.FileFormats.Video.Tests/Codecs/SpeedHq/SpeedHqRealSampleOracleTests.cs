using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes SpeedHQ films here and in FFmpeg and requires every sample to match.</summary>
/// <remarks>
/// Opt-in: point <c>SPEEDHQ_SAMPLES</c> at a directory with <c>420</c>, <c>422</c> and <c>444</c>
/// subdirectories of SpeedHQ films — real NDI captures, or ones FFmpeg's own SpeedHQ encoder wrote — each
/// compared in its own planar layout.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class SpeedHqRealSampleOracleTests {

  [TestCase("420", "yuv420p")]
  [TestCase("422", "yuv422p")]
  [TestCase("444", "yuv444p")]
  [Explicit("Needs SpeedHQ films; point SPEEDHQ_SAMPLES at a directory of them.")]
  public void EveryPictureOfASpeedHqFilmMatchesFFmpeg(string subsampling, string pixelFormat) {
    var root = System.Environment.GetEnvironmentVariable("SPEEDHQ_SAMPLES");
    if (string.IsNullOrWhiteSpace(root))
      Assert.Inconclusive("Set SPEEDHQ_SAMPLES to a directory of SpeedHQ films.");
    System.Environment.SetEnvironmentVariable("SPEEDHQ_SAMPLES_" + subsampling, System.IO.Path.Combine(root!, subsampling));
    RealSampleComparison.Run("SPEEDHQ_SAMPLES_" + subsampling, SpeedHqVideoDecoder.Accepts, pixelFormat);
  }

  /// <summary>
  /// FFmpeg's own FATE samples, a progressive and a single-field SpeedHQ 4:2:2 frame, against the
  /// <c>framecrc</c> checksums FFmpeg commits for them in <c>tests/ref/fate/speedhq-422</c> and
  /// <c>speedhq-422-singlefield</c> — Adler-32 of the <c>yuv422p</c> picture, started from zero.
  /// </summary>
  [TestCase("progressive.shq2", 112, 64, 0x9bb6dc6du)]
  [TestCase("singlefield.shq2", 112, 32, 0x75de4109u)]
  [Explicit("Needs FFmpeg's FATE SpeedHQ samples under SPEEDHQ_SAMPLES/raw.")]
  public void TheFateSamplesDecodeToFFmpegsCommittedChecksums(string file, int width, int height, uint expected) {
    var root = System.Environment.GetEnvironmentVariable("SPEEDHQ_SAMPLES");
    var path = root == null ? null : System.IO.Path.Combine(root, "raw", file);
    if (path == null || !System.IO.File.Exists(path))
      Assert.Inconclusive("Put FFmpeg's FATE speedhq/*.shq2 samples under SPEEDHQ_SAMPLES/raw.");

    var decoder = SpeedHqVideoDecoder.Create(new FileFormat.Core.MediaStreamInfo {
      Index = 0,
      Kind = FileFormat.Core.MediaStreamKind.Video,
      Codec = FileFormat.Core.CodecTag.FromCharacters("SHQ2"),
      Width = width,
      Height = height,
    });
    decoder.TryDecode(new(0, System.IO.File.ReadAllBytes(path!)), out var picture);

    uint a = 0, b = 0;
    foreach (var value in picture.PixelData) {
      a = (a + value) % 65521;
      b = (b + a) % 65521;
    }
    Assert.That((b << 16) | a, Is.EqualTo(expected));
  }
}
