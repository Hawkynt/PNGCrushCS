using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Mpeg.Tests;

/// <summary>
/// Cross-checks the MPEG-2 syntax that is difficult to obtain from commodity encoders against
/// FFmpeg's independent decoder. The streams themselves are original test code derived from H.262;
/// FFmpeg is used only as a behavioral oracle.
/// </summary>
[TestFixture]
public sealed class Mpeg2VideoDecoderOracleTests {

  private const int _TIMEOUT_MILLISECONDS = 60_000;

  [Test]
  [Category("Oracle")]
  public void FFmpegAgreesOnAnIThenPFieldCodedFrame() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 32).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1)
      .PictureCodingExtension(pictureStructure: 1, framePredFrameDct: false, progressiveFrame: false)
      .SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1, luminanceDifferential: 8);

    stream
      .PictureHeader(2, forwardFCode: 1)
      .PictureCodingExtension(
        forwardFCode: 1, pictureStructure: 2, framePredFrameDct: false, progressiveFrame: false)
      .SliceHeader(0, 1)
      .Code("1").Code("001")
      .Bits(1, 2)
      .Bits(0, 1)
      .Code("1").Code("1");

    _AssertMatchesFFmpeg(stream.End(), 16, 32);
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegAgreesOnFrameDualPrimePrediction() {
    const int size = 64;
    var stream = new MpegTestStream()
      .SequenceHeader(size, size).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1).PictureCodingExtension(progressiveFrame: false);

    for (var row = 0; row < 4; ++row) {
      stream.SliceHeader(row, 1);
      _FlatIntraMacroblocks(stream, 4);
    }

    stream
      .PictureHeader(2, forwardFCode: 1)
      .PictureCodingExtension(forwardFCode: 1, framePredFrameDct: false, progressiveFrame: false);

    for (var row = 0; row < 4; ++row) {
      stream.SliceHeader(row, 1);
      for (var column = 0; column < 4; ++column) {
        stream.Code("1").Code("001");
        if (row is 1 or 2)
          stream.Bits(3, 2).Code("1").Code("1").Bits(0, 1).Bits(0, 1);
        else
          stream.Bits(2, 2).Code("1").Code("1");
      }
    }

    _AssertMatchesFFmpeg(stream.End(), size, size);
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegAgreesOnTheDefinedFourFourFourSyntax() {
    // H.262 defines 4:4:4 syntax and reconstruction but, per Annex D.2, assigns it to no profile.
    // 0x18 therefore does not turn this into a conforming High Profile stream; it merely gives the
    // sequence extension an otherwise ordinary profile/level byte so both decoders can exercise the
    // defined chroma_format=3 syntax as an interoperability extension.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16)
      .SequenceExtension(chromaFormat: 3, profileAndLevel: 0x18)
      .PictureHeader(1)
      .PictureCodingExtension()
      .SliceHeader(0, 1)
      .Code("1").Code("1");

    stream
      .IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
      .IntraBlock(false, 8).IntraBlock(false, 0)
      .IntraBlock(false, -16).IntraBlock(false, 0)
      .IntraBlock(false, 24).IntraBlock(false, 0)
      .IntraBlock(false, -16).IntraBlock(false, 0);

    _AssertMatchesFFmpeg(stream.End(), 16, 16);
  }

  private static void _AssertMatchesFFmpeg(byte[] stream, int width, int height) {
    FFmpegOracle.RequireAvailable();

    var managed = _DecodeManaged(stream);
    var oracle = _DecodeWithFFmpeg(stream, width, height, managed.Count);
    var expected = managed.SelectMany(static frame => frame.PixelData).ToArray();

    Assert.That(oracle, Is.EqualTo(expected));
  }

  private static List<RawImage> _DecodeManaged(byte[] stream) {
    var decoder = new Mpeg2VideoDecoder();
    var frames = new List<RawImage>();
    if (decoder.TryDecode(new(0, stream), out var frame))
      frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  private static byte[] _DecodeWithFFmpeg(byte[] stream, int width, int height, int expectedFrames) {
    var input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".m2v");
    var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".rgb");

    try {
      File.WriteAllBytes(input, stream);
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", input,
        "-map", "0:v:0", "-an", "-sn", "-dn", "-fps_mode", "passthrough",
        "-f", "rawvideo", "-pix_fmt", "rgb24", output,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("ffmpeg would not start");
      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();

      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Fail("ffmpeg timed out");
      }

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      Assert.That(process.ExitCode, Is.Zero, diagnostics);
      Assert.That(diagnostics, Is.Empty);
      Assert.That(File.Exists(output), Is.True, "ffmpeg produced no raw video");

      var bytes = File.ReadAllBytes(output);
      Assert.That(bytes.Length, Is.EqualTo(checked(width * height * 3 * expectedFrames)));
      return bytes;
    } catch (System.ComponentModel.Win32Exception) {
      Assert.Inconclusive("ffmpeg disappeared after the oracle availability check");
      return [];
    } finally {
      try { File.Delete(input); } catch { /* best effort */ }
      try { File.Delete(output); } catch { /* best effort */ }
    }
  }

  private static void _FlatIntraMacroblocks(MpegTestStream stream, int count, int luminanceDifferential = 0) {
    for (var i = 0; i < count; ++i) {
      stream.Code("1").Code("1");
      var differential = i == 0 ? luminanceDifferential : 0;
      stream.IntraBlock(true, differential).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0);
      stream.IntraBlock(false, 0).IntraBlock(false, 0);
    }
  }
}
