using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// Runs FFmpeg for the ProRes oracles: writing its own files, and decoding files to component planes.
/// </summary>
/// <remarks>
/// Both ProRes oracles need the same two calls and neither of them may quietly become a round trip
/// through this package, so the calls live here once rather than twice with a chance to drift.
/// <para/>
/// <b><c>-fps_mode</c>, never <c>-vsync</c>.</b> The old spelling was removed in FFmpeg 7, where it
/// aborts the command before a frame has been read; a check whose tool never ran reports whatever
/// its author expected rather than whatever is true, which is the worst way for an oracle to fail.
/// </remarks>
internal static class ProResFFmpegFixtures {

  private const int _TIMEOUT_MILLISECONDS = 60_000;

  /// <summary>Writes one ProRes frame with FFmpeg's own encoder and returns the file it is in.</summary>
  /// <param name="profile">The profile as <c>prores_ks</c> numbers them: 0 Proxy to 5 4444 XQ.</param>
  /// <param name="width">The picture width.</param>
  /// <param name="height">The picture height.</param>
  /// <param name="alphaBits">Alpha sample depth: 16, 8, or 0 for a stream with no alpha channel.</param>
  /// <param name="fieldMode">"tff", "bff", or null for a progressive frame.</param>
  /// <remarks>
  /// The source is generated here rather than taken from a <c>lavfi</c> pattern, and it is deliberately
  /// band-limited — see <see cref="SmoothSource"/>. What the comparison afterwards has to be able to
  /// resolve is a defect in this package's reader, so the only other thing allowed to move the samples
  /// is the difference between two inverse transforms, and that difference is a function of how much
  /// high-frequency energy the picture has.
  /// </remarks>
  internal static string Write(
    int profile,
    int width,
    int height,
    int alphaBits = 0,
    string? fieldMode = null) {
    var directory = Directory.CreateTempSubdirectory("prores-ffmpeg");
    var movie = Path.Combine(directory.FullName, "ffmpeg.mov");
    var sourceFormat = profile < 4
      ? "yuv422p10le"
      : alphaBits == 0 ? "yuv444p10le" : "yuva444p10le";
    var source = Path.Combine(directory.FullName, $"source.{sourceFormat}");
    File.WriteAllBytes(source, SmoothSource(width, height, sourceFormat));

    var arguments = new List<string> {
      "-hide_banner", "-loglevel", "error", "-y", "-threads", "1",
      "-f", "rawvideo", "-pix_fmt", sourceFormat, "-s", $"{width}x{height}", "-r", "25", "-i", source,
    };

    // A filter rather than an encoder option: prores_ks takes the field order from the frame it is
    // handed, not from a flag of its own.
    if (fieldMode != null)
      arguments.AddRange(["-vf", $"setparams=field_mode={fieldMode}", "-flags", "+ilme+ildct"]);

    arguments.AddRange([
      "-c:v", "prores_ks", "-profile:v", profile.ToString(),
      "-fps_mode", "passthrough", "-frames:v", "1",
    ]);
    if (profile >= 4)
      arguments.AddRange(["-alpha_bits", alphaBits.ToString()]);
    arguments.Add(movie);

    _Run(arguments, "write a ProRes frame");
    Assert.That(File.Exists(movie), Is.True, "ffmpeg wrote no ProRes file");

    return movie;
  }

  /// <summary>
  /// A band-limited picture: every block distinct, no step this codec's transform cannot represent.
  /// </summary>
  /// <remarks>
  /// Two requirements pull against each other here. The picture has to distinguish the blocks,
  /// macroblocks and planes from one another, or a reader that transposed, rotated or interleaved them
  /// would reproduce the picture anyway and the comparison would pass while proving nothing. And it
  /// has to stay inside what an 8x8 transform represents comfortably, because FFmpeg reconstructs with
  /// an integer approximation of RDD 36's inverse transform and this package evaluates it directly, so
  /// a hard edge makes the two disagree by several levels through nothing but ringing and turns the
  /// tolerance into a number too loose to catch anything.
  /// <para/>
  /// A plane ramp plus one gentle sinusoid per axis satisfies both: the ramp gives every block its own
  /// DC and every plane its own gradient, the sinusoid's sixteen- and twenty-four-sample periods put
  /// real energy in the low-order AC coefficients, and nothing in it has a discontinuity. Measured,
  /// the two transforms then differ by at most one level, which is tight enough that a mis-ordered
  /// chroma block or a mis-mapped field misses by two orders of magnitude.
  /// </remarks>
  internal static byte[] SmoothSource(int width, int height, string pixelFormat) {
    var fourFourFour = pixelFormat.Contains("444", StringComparison.Ordinal);
    var alpha = pixelFormat.StartsWith("yuva", StringComparison.Ordinal);
    var planes = SmoothPlanes(width, height, fourFourFour, alpha, bitDepth: 10);

    var bytes = new byte[planes.Sum(static plane => plane.Length) * 2];
    var at = 0;
    foreach (var plane in planes)
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), sample);
        at += 2;
      }

    return bytes;
  }

  /// <summary>
  /// The same band-limited picture as component planes, at ten or twelve bits.
  /// </summary>
  /// <remarks>
  /// Luma, then the two chroma planes, then the matte where one was asked for. The matte is a ramp
  /// too: alpha is coded losslessly, so anything at all survives it, and a ramp is simply what makes a
  /// dropped or transposed one visible.
  /// </remarks>
  internal static ushort[][] SmoothPlanes(int width, int height, bool fourFourFour, bool alpha, int bitDepth) {
    var chromaWidth = fourFourFour ? width : (width + 1) / 2;
    var scale = bitDepth == 12 ? 4 : 1;
    var ceiling = (1 << bitDepth) - 5;

    var planes = new List<ushort[]> {
      _Plane(width, height, 140, 3.5, 4.5, 90, 16, 24, scale, ceiling),
      _Plane(chromaWidth, height, 300, fourFourFour ? 2.5 : 5.0, 2.5, 70, 24, 16, scale, ceiling),
      _Plane(chromaWidth, height, 700, fourFourFour ? -2.0 : -4.0, -3.0, 60, 16, 32, scale, ceiling),
    };
    if (alpha)
      planes.Add(_Plane(width, height, 500, 2.0, 3.0, 120, 32, 16, scale, ceiling));

    return [.. planes];
  }

  private static ushort[] _Plane(
    int width,
    int height,
    double offset,
    double perColumn,
    double perRow,
    double amplitude,
    double columnPeriod,
    double rowPeriod,
    int scale,
    int ceiling) {
    var plane = new ushort[checked(width * height)];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = offset
          + perColumn * x
          + perRow * y
          + amplitude * Math.Sin(2 * Math.PI * x / columnPeriod)
          + amplitude * Math.Sin(2 * Math.PI * y / rowPeriod);
        plane[y * width + x] = (ushort)Math.Clamp((int)Math.Round(value) * scale, 4, ceiling);
      }

    return plane;
  }

  /// <summary>Decodes a file to raw component planes at the depth the codec coded them at.</summary>
  internal static byte[] DecodeToRaw(string movie, string pixelFormat) {
    var raw = Path.Combine(Path.GetDirectoryName(movie)!, $"decoded-{pixelFormat}.raw");

    _Run([
      "-hide_banner", "-loglevel", "error", "-y", "-threads", "1", "-i", movie,
      "-map", "0:v:0", "-an", "-sn", "-dn", "-fps_mode", "passthrough", "-frames:v", "1",
      "-f", "rawvideo", "-pix_fmt", pixelFormat, raw,
    ], "decode a ProRes frame");

    Assert.That(File.Exists(raw), Is.True, "ffmpeg produced no raw component planes");

    return File.ReadAllBytes(raw);
  }

  /// <summary>Removes the temporary directory a fixture file was written into.</summary>
  internal static void Discard(string file) {
    try {
      Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    } catch (Exception) {
      // A leftover temporary directory is not worth failing a test over.
    }
  }

  private static void _Run(IReadOnlyList<string> arguments, string what) {
    var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var argument in arguments)
      startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo);
    Assert.That(process, Is.Not.Null, "ffmpeg would not start");

    var stdout = process!.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
      try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
      Assert.Fail($"ffmpeg timed out trying to {what}");
    }

    var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
    Assert.Multiple(() => {
      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused to {what}: {diagnostics}");
      Assert.That(diagnostics, Is.Empty, $"ffmpeg managed to {what} only after reporting an error: {diagnostics}");
    });
  }
}
