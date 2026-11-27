using System;
using System.IO;
using System.Text;

namespace FileFormat.Wic.Tests;

/// <summary>Fixture access and the binary PNM files the oracles write.</summary>
internal static class WicTestSupport {

  public static string FixturePath(string name)
    => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Wic", name);

  public static byte[] Fixture(string name) => File.ReadAllBytes(FixturePath(name));

  /// <summary>Reads a binary P5 (grey) or P6 (RGB) file with an 8-bit maximum.</summary>
  public static (int Width, int Height, bool Colour, byte[] Pixels) ReadPnm(byte[] data) {
    var position = 0;

    string Token() {
      while (position < data.Length && (char.IsWhiteSpace((char)data[position]) || data[position] == '#')) {
        if (data[position] == '#')
          while (position < data.Length && data[position] != '\n')
            ++position;
        else
          ++position;
      }

      var start = position;
      while (position < data.Length && !char.IsWhiteSpace((char)data[position]))
        ++position;
      return Encoding.ASCII.GetString(data, start, position - start);
    }

    var magic = Token();
    if (magic is not ("P5" or "P6"))
      throw new InvalidDataException($"Not a binary PNM: {magic}");

    var width = int.Parse(Token());
    var height = int.Parse(Token());
    _ = Token();
    ++position;
    var colour = magic == "P6";
    var pixels = data.AsSpan(position, width * height * (colour ? 3 : 1)).ToArray();
    return (width, height, colour, pixels);
  }

  public static byte[] WritePnm(int width, int height, bool colour, byte[] pixels) {
    var header = Encoding.ASCII.GetBytes($"P{(colour ? 6 : 5)}\n{width} {height}\n255\n");
    var result = new byte[header.Length + pixels.Length];
    header.CopyTo(result, 0);
    pixels.CopyTo(result, header.Length);
    return result;
  }

  /// <summary>Largest per-sample difference between two equally sized pictures.</summary>
  public static int MaxDifference(byte[] a, byte[] b) {
    Assert.That(a, Has.Length.EqualTo(b.Length));
    var max = 0;
    for (var i = 0; i < a.Length; ++i)
      max = Math.Max(max, Math.Abs(a[i] - b[i]));
    return max;
  }

  /// <summary>Peak signal-to-noise ratio in dB.</summary>
  public static double Psnr(byte[] a, byte[] b) {
    double sum = 0;
    for (var i = 0; i < a.Length; ++i) {
      var d = a[i] - b[i];
      sum += d * d;
    }

    return sum == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 * a.Length / sum);
  }
}
