using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>Temporary one-shot generator; removed after its output is committed as fixed vectors.</summary>
[TestFixture]
public class Ffv1OracleVectorGeneratorTests {

  [Test]
  [Category("Unit")]
  public void GenerateOracleVectors() {
    if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/ffmpeg"))
      Assert.Ignore("FFmpeg oracle generation runs once on the Linux CI image.");

    var cases = new[] {
      new VectorCase("v0_rice_small", 1, "gray", ["-level", "0", "-coder", "rice", "-context", "0", "-g", "1"]),
      new VectorCase("v1_range_default", 1, "gray", ["-level", "1", "-coder", "range_def", "-context", "0", "-g", "1"]),
      new VectorCase("v3_rice", 1, "gray", ["-level", "3", "-coder", "rice", "-context", "0", "-slicecrc", "1", "-g", "1"]),
      new VectorCase("v3_range_large", 1, "gray", ["-level", "3", "-coder", "range_def", "-context", "1", "-slicecrc", "1", "-g", "1"]),
      new VectorCase("v3_range_custom", 1, "gray", ["-level", "3", "-coder", "range_tab", "-context", "0", "-slicecrc", "1", "-g", "1"]),
      new VectorCase("v3_gop", 4, "gray", ["-level", "3", "-coder", "range_def", "-context", "0", "-slicecrc", "1", "-g", "3"]),
      new VectorCase("v3_gray10", 1, "gray10le", ["-level", "3", "-coder", "range_def", "-context", "0", "-slicecrc", "1", "-g", "1"]),
      new VectorCase("v3_yuv12", 1, "yuv420p12le", ["-level", "3", "-coder", "range_def", "-context", "1", "-slicecrc", "1", "-g", "1"]),
      new VectorCase("v3_rgb16", 1, "gbrp16le", ["-level", "3", "-coder", "range_def", "-context", "0", "-slicecrc", "1", "-g", "1"]),
    };

    var output = new StringBuilder();
    foreach (var item in cases) {
      var path = Path.Combine(Path.GetTempPath(), $"ffv1-{item.Name}-{Guid.NewGuid():N}.mkv");
      try {
        var args = new List<string> {
          "-hide_banner", "-loglevel", "error", "-y",
          "-f", "lavfi", "-i", $"testsrc2=size=16x12:rate=1:duration={item.Frames}",
          "-frames:v", item.Frames.ToString(),
          "-vf", $"format={item.PixelFormat}",
          "-c:v", "ffv1",
        };
        args.AddRange(item.CodecArguments);
        args.AddRange(["-f", "matroska", path]);
        _Run("/usr/bin/ffmpeg", args);
        output.Append(item.Name).Append('=').Append(Convert.ToBase64String(File.ReadAllBytes(path))).AppendLine();
      } finally {
        if (File.Exists(path))
          File.Delete(path);
      }
    }

    Assert.Fail("FFV1_ORACLE_VECTORS\n" + output);
  }

  private static void _Run(string program, IEnumerable<string> arguments) {
    var start = new ProcessStartInfo(program) {
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
    };
    foreach (var argument in arguments)
      start.ArgumentList.Add(argument);

    using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {program}.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
      Assert.Fail($"{program} exited {process.ExitCode}:\n{stdout}\n{stderr}");
  }

  private sealed record VectorCase(string Name, int Frames, string PixelFormat, string[] CodecArguments);
}