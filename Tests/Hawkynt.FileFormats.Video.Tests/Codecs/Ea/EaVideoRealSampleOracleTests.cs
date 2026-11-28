using System;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Decodes real Electronic Arts films here and in FFmpeg and requires every sample to match.</summary>
/// <remarks>
/// Opt-in: point <c>EA_SAMPLES</c> at a directory with subdirectories <c>tqi</c>, <c>tgq</c>, <c>mad</c> and
/// <c>tgv</c>, holding the films of <c>samples.ffmpeg.org/game-formats/ea-*</c> and the FATE suite's <c>ea-*</c>.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class EaVideoRealSampleOracleTests {

  [TestCase("tqi")]
  [TestCase("tgq")]
  [TestCase("mad")]
  [Explicit("Needs a directory of real Electronic Arts films; point EA_SAMPLES at one.")]
  public void EveryPictureOfARealEaFilmMatchesFFmpeg(string codec) {
    var root = System.Environment.GetEnvironmentVariable("EA_SAMPLES");
    if (string.IsNullOrWhiteSpace(root))
      Assert.Inconclusive("Set EA_SAMPLES to a directory of real Electronic Arts films.");
    System.Environment.SetEnvironmentVariable("EA_SAMPLES_" + codec, System.IO.Path.Combine(root!, codec));
    System.Func<FileFormat.Core.MediaStreamInfo, bool> accepts = codec switch {
      "tqi" => EaTqiVideoDecoder.Accepts,
      "mad" => EaMadVideoDecoder.Accepts,
      _ => EaTgqVideoDecoder.Accepts,
    };
    RealSampleComparison.Run("EA_SAMPLES_" + codec, accepts, "yuv420p");
  }

  /// <summary>TGV pictures are palettised, so they are compared as FFmpeg's RGB.</summary>
  [Test]
  [Explicit("Needs a directory of real Electronic Arts films; point EA_SAMPLES at one.")]
  public void EveryPictureOfARealTgvFilmMatchesFFmpeg() {
    FFmpegOracle.RequireAvailable();
    var root = System.Environment.GetEnvironmentVariable("EA_SAMPLES");
    if (string.IsNullOrWhiteSpace(root))
      Assert.Inconclusive("Set EA_SAMPLES to a directory of real Electronic Arts films.");
    var total = 0;
    var failures = new System.Collections.Generic.List<string>();
    foreach (var film in System.IO.Directory.GetFiles(System.IO.Path.Combine(root!, "tgv"))) {
      var bytes = System.IO.File.ReadAllBytes(film);
      var stream = System.Linq.Enumerable.First(Hawkynt.FileFormats.Video.VideoFormatRegistry.ReadStreams(bytes), static s => s.Kind == FileFormat.Core.MediaStreamKind.Video);
      var decoder = EaTgvVideoDecoder.Create(stream);
      var ours = new System.Collections.Generic.List<byte[]>();
      foreach (var packet in Hawkynt.FileFormats.Video.VideoFormatRegistry.ReadPackets(bytes, stream.Index))
        if (decoder.TryDecode(packet, out var picture))
          ours.Add(picture.ToRgb24());
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, "rgb24", tolerateDiagnostics: true);
      Assert.That(decoded, Is.True, detail);
      var size = ours.Count > 0 ? ours[0].Length : 1;
      var compared = 0;
      for (; compared < ours.Count && (compared + 1) * size <= samples.Length; ++compared)
        if (!samples.AsSpan(compared * size, size).SequenceEqual(ours[compared])) {
          failures.Add($"{System.IO.Path.GetFileName(film)}: picture {compared} differs");
          break;
        }
      var line = $"{System.IO.Path.GetFileName(film)}: {compared} of {ours.Count} pictures identical to ffmpeg, which has {samples.Length / size}";
      TestContext.Out.WriteLine(line);
      if (compared != ours.Count || samples.Length != ours.Count * size)
        failures.Add(line);
      total += compared;
    }
    Assert.That(failures, Is.Empty, string.Join("\n", failures));
  }
}
