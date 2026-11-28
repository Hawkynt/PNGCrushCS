using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FileFormat.Iwc.Tests;

/// <summary>Fixture access, the P6 files the applet harness writes, and the oracle command.</summary>
internal static class IwcTestSupport {

  public static string FixturePath(string name)
    => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Iwc", name);

  public static byte[] Fixture(string name) => File.ReadAllBytes(FixturePath(name));

  /// <summary>Reads a binary P6 file with an 8-bit maximum.</summary>
  public static (int Width, int Height, byte[] Pixels) ReadPpm(byte[] data) {
    var position = 0;

    string Token() {
      while (position < data.Length && char.IsWhiteSpace((char)data[position]))
        ++position;
      var start = position;
      while (position < data.Length && !char.IsWhiteSpace((char)data[position]))
        ++position;
      return Encoding.ASCII.GetString(data, start, position - start);
    }

    if (Token() != "P6")
      throw new InvalidDataException("Not a binary PPM.");

    var width = int.Parse(Token());
    var height = int.Parse(Token());
    _ = Token();
    ++position;
    return (width, height, data.AsSpan(position, width * height * 3).ToArray());
  }

  public static byte[] WritePpm(int width, int height, byte[] pixels) {
    var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
    var result = new byte[header.Length + pixels.Length];
    header.CopyTo(result, 0);
    pixels.CopyTo(result, header.Length);
    return result;
  }

  public static int MaxDifference(byte[] a, byte[] b) {
    Assert.That(a, Has.Length.EqualTo(b.Length));
    var max = 0;
    for (var i = 0; i < a.Length; ++i)
      max = Math.Max(max, Math.Abs(a[i] - b[i]));
    return max;
  }

  /// <summary>
  /// The command <c>IWC_ORACLE</c> names, run as <c>command in.iwc out.ppm</c>: a wrapper around the
  /// WaveL applet's own classes (<c>Decompr.Decompress</c>, pixels taken from its <c>pixels</c> field).
  /// </summary>
  public static byte[]? RunOracle(string input) {
    var command = Environment.GetEnvironmentVariable("IWC_ORACLE");
    if (string.IsNullOrWhiteSpace(command) || !File.Exists(command))
      return null;

    var output = Path.ChangeExtension(input, ".oracle.ppm");
    using var process = Process.Start(new ProcessStartInfo(command, [input, output]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
    process.WaitForExit(60_000);
    return File.Exists(output) ? File.ReadAllBytes(output) : [];
  }
}
