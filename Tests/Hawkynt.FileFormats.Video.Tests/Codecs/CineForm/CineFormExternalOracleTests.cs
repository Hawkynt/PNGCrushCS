using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;
using NUnit.Framework;

namespace FileFormat.Codecs.CineForm.Tests;

/// <summary>Crosses the implementation boundary in both directions for the twelve-bit CineForm layouts.</summary>
[TestFixture]
[Category("Conformance")]
public sealed class CineFormExternalOracleTests {

  [TestCase("gbrp12le", PixelFormat.Rgb24)]
  [TestCase("gbrap12le", PixelFormat.Rgba32)]
  public void DecoderReadsAFrameWrittenByFfmpeg(string pixelFormat, PixelFormat expectedOutput) {
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", $"color=c=0x804020:s={width}x{height}:r=25",
        "-frames:v", "1", "-c:v", "cfhd", "-pix_fmt", pixelFormat,
        path,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      Assert.That(process, Is.Not.Null, "ffmpeg would not start");

      var stdout = process!.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(60_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Fail("ffmpeg timed out while producing a CineForm oracle frame");
      }

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      Assert.That(process.ExitCode, Is.Zero, diagnostics);
      Assert.That(diagnostics, Is.Empty, diagnostics);

      var avi = VideoIO.Read<AviReader>(File.ReadAllBytes(path));
      var stream = VideoIO.FirstVideoStream(avi);
      Assert.That(stream, Is.Not.Null, "ffmpeg AVI did not expose a video stream");
      Assert.That(stream!.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("CFHD")), Is.True);

      var decoded = VideoIO.DecodeStream<AviReader, CineFormVideoDecoder>(avi, stream).Single();
      Assert.Multiple(() => {
        Assert.That(decoded.Image.Width, Is.EqualTo(width));
        Assert.That(decoded.Image.Height, Is.EqualTo(height));
        Assert.That(decoded.Image.Format, Is.EqualTo(expectedOutput));
      });
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }
}
