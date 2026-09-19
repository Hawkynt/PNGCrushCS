using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Flv;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;
using Hawkynt.FileFormats.Video.Tests.Codecs;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class FlashSv2VideoEncoderOracleTests {

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsEveryFrameExactly() {
    FFmpegOracle.RequireAvailable();

    const int width = 70;
    const int height = 37;
    const int frameCount = 14;

    var frames = LosslessEncoderPictures.Sequence(width, height, PixelFormat.Bgr24, frameCount, seed: 0xF5A2);
    var encoder = FlashSv2VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = width,
      Height = height,
      TimeBase = new Rational(1, 1000),
      FrameRate = new Rational(10, 1),
    });

    var packets = frames.Select((frame, index) => {
      Assert.That(encoder.TryEncode(frame, index * 100, out var packet), Is.True);
      return packet;
    }).ToArray();

    var directory = Directory.CreateTempSubdirectory("flashsv2-oracle");
    try {
      var input = Path.Combine(directory.FullName, "clip.flv");
      var output = Path.Combine(directory.FullName, "decoded.bgr");
      File.WriteAllBytes(input, VideoIO.Mux<FlvWriter>([encoder.DescribeStream()], packets));

      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", input,
        "-map", "0:v:0", "-an", "-sn", "-dn", "-fps_mode", "passthrough",
        "-f", "rawvideo", "-pix_fmt", "bgr24", output,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      Assert.That(process, Is.Not.Null, "ffmpeg would not start");

      var stdout = process!.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(60_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Fail("ffmpeg timed out while decoding the Flash Screen Video 2 stream");
      }

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      Assert.Multiple(() => {
        Assert.That(process.ExitCode, Is.Zero, diagnostics);
        Assert.That(diagnostics, Is.Empty, "ffmpeg reported a decode error or repair");
      });

      var decoded = File.ReadAllBytes(output);
      var frameBytes = width * height * 3;
      Assert.That(decoded, Has.Length.EqualTo(frameBytes * frameCount), "ffmpeg returned every coded frame");

      for (var index = 0; index < frameCount; ++index)
        Assert.That(
          decoded.AsSpan(index * frameBytes, frameBytes).ToArray(),
          Is.EqualTo(frames[index].PixelData),
          $"ffmpeg's decoded frame {index} differs from the BGR pixels given to the encoder");
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
