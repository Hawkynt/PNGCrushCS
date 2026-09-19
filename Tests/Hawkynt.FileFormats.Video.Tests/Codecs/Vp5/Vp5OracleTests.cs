using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Vp5.Tests;

/// <summary>
/// What FFmpeg makes of three real VP5 clips, frame by frame, against what this decoder makes of them.
/// </summary>
/// <remarks>
/// <b>Why an outside decoder and not a picture somebody looked at.</b> VP5 has no published bitstream
/// specification, so "it looks right" is the whole of what inspection can say, and a decoder that
/// desynchronises a little way into a long still passage looks right for a long time. The only thing
/// that separates a plausible decode from a correct one is a second implementation, and the only
/// complete second implementation of VP5 that exists is FFmpeg's. The clips and the digests are
/// described in <see cref="Vp5Fixtures"/>.
/// <para/>
/// <b>Planes and not RGB.</b> The RGB a decoder hands back is a display convention — this package
/// interpolates chrominance where FFmpeg repeats it — so an RGB comparison has a floor that grows
/// with how much colour the picture holds and says nothing about whether the bitstream was read. The
/// planes are the decode.
/// <para/>
/// <b>Every frame and not the first.</b> A key frame decoding correctly says nothing about the
/// prediction loop, which is where a wrong reconstruction compounds. What is committed here is the
/// short form of a longer measurement: the same comparison over the whole of both source files —
/// 2084 frames of <c>potter512-400.avi</c> and 2395 of <c>vp5_interlace.avi</c> — is byte-identical on
/// every plane of every frame. Those files are 4.4 and 23 MB and are not carried around.
/// </remarks>
[TestFixture]
public sealed class Vp5OracleTests {

  [TestCase(Vp5Fixtures.SIXTY_FRAMES)]
  [TestCase(Vp5Fixtures.THREE_KEY_FRAMES)]
  [TestCase(Vp5Fixtures.INTERLACED)]
  [Category("Unit")]
  public void EveryFrameOfARealClipDecodesToWhatFFmpegSaysItDoes(string fixture) {
    var expected = Vp5Fixtures.ExpectedFrameDigests(fixture);
    var actual = Vp5Fixtures.Decode(fixture).Select(Vp5Fixtures.Digest).ToList();

    Assert.That(actual, Has.Count.EqualTo(expected.Count),
      $"'{fixture}' decoded {actual.Count} frames where FFmpeg decoded {expected.Count}.");

    var wrong = Enumerable.Range(0, expected.Count).Where(i => actual[i] != expected[i]).ToList();
    Assert.That(wrong, Is.Empty,
      $"'{fixture}': {wrong.Count} of {expected.Count} frames differ from FFmpeg's decode, first at frame "
      + $"{(wrong.Count > 0 ? wrong[0] : -1)}.");
  }

  /// <summary>
  /// The comparison above has to be able to fail, so here it is being made to.
  /// </summary>
  /// <remarks>
  /// A test that holds one thing against another is worth exactly as much as the demonstration that
  /// it notices a difference. One bit of one sample of one plane of one frame is the smallest change
  /// a wrong decode could possibly produce, and the digest has to catch it.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void OneWrongBitInOneFrameIsNoticed() {
    var expected = Vp5Fixtures.ExpectedFrameDigests(Vp5Fixtures.SIXTY_FRAMES);
    var (luma, cb, cr) = Vp5Fixtures.Decode(Vp5Fixtures.SIXTY_FRAMES)[17];

    Assert.That(Vp5Fixtures.Digest((luma, cb, cr)), Is.EqualTo(expected[17]),
      "frame 17 did not agree with FFmpeg before it was damaged, so damaging it proves nothing.");

    luma[luma.Length / 2] ^= 1;

    Assert.That(Vp5Fixtures.Digest((luma, cb, cr)), Is.Not.EqualTo(expected[17]),
      "the digest did not notice a single flipped bit, so it cannot be noticing a wrong decode either.");
  }

  /// <summary>
  /// The same claim again, made against the FFmpeg on this machine rather than against a file.
  /// </summary>
  /// <remarks>
  /// The committed digests are what a build machine without FFmpeg can check, and they are only as
  /// good as the run that produced them. Where FFmpeg is present the tool itself is asked, now, so
  /// that the committed file cannot quietly become a record of a decoder agreeing with its own past
  /// mistake. Where it is absent the check reports inconclusive and the rest of the suite runs
  /// untouched.
  /// </remarks>
  [TestCase(Vp5Fixtures.SIXTY_FRAMES)]
  [TestCase(Vp5Fixtures.THREE_KEY_FRAMES)]
  [TestCase(Vp5Fixtures.INTERLACED)]
  [Category("Unit")]
  public void TheFFmpegOnThisMachineAgreesWithTheCommittedDigests(string fixture) {
    FFmpegOracle.RequireAvailable();

    Assert.That(
      _RunFFmpegFrameDigests(Vp5Fixtures.Path(fixture + ".avi")),
      Is.EqualTo(Vp5Fixtures.ExpectedFrameDigests(fixture)).AsCollection,
      $"the FFmpeg on this machine decodes '{fixture}' differently from the one that wrote the committed digests.");
  }

  // ==============================================================================================

  private static List<string> _RunFFmpegFrameDigests(string path) {
    var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".framemd5");

    try {
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", path,
        "-an", "-pix_fmt", "yuv420p", "-fps_mode", "passthrough", "-f", "framemd5", output,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      Assert.That(process, Is.Not.Null, "ffmpeg would not start.");

      var diagnostics = process!.StandardError.ReadToEndAsync();
      var chatter = process.StandardOutput.ReadToEndAsync();
      if (!process.WaitForExit(120_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Inconclusive("ffmpeg timed out.");
      }

      Assert.That(string.Concat(diagnostics.Result, chatter.Result).Trim(), Is.Empty,
        "ffmpeg complained about a file it is supposed to read cleanly.");

      return Vp5Fixtures.ParseFrameMd5(File.ReadAllLines(output));
    } finally {
      try { File.Delete(output); } catch { /* best effort */ }
    }
  }
}
