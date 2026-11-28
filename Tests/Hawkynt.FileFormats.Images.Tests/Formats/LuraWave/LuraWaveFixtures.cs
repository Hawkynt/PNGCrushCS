using System;
using System.IO;
using System.Text;

namespace FileFormat.LuraWave.Tests;

/// <summary>Loads the committed LuraWave fixtures and the binary PNM pictures beside them.</summary>
internal static class LuraWaveFixtures {

  public static byte[] Bytes(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "LuraWave", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>Parses a binary PGM (P5, 8 or 16 bit) or PPM (P6, 8 bit); 16-bit samples stay big-endian.</summary>
  public static (int Width, int Height, int Channels, int MaxValue, byte[] Pixels) Pnm(byte[] data) {
    var position = 0;

    string Token() {
      while (char.IsWhiteSpace((char)data[position]))
        ++position;
      var start = position;
      while (!char.IsWhiteSpace((char)data[position]))
        ++position;
      return Encoding.ASCII.GetString(data, start, position - start);
    }

    var magic = Token();
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    var maxValue = int.Parse(Token());
    ++position;
    var channels = magic == "P6" ? 3 : 1;
    return (width, height, channels, maxValue, data[position..]);
  }

  /// <summary>
  /// Reads a PAM (P7) holding channel samples: one byte each up to MAXVAL 255, otherwise two bytes big-endian.
  /// </summary>
  public static (int Width, int Height, ushort[] Samples) Pam(byte[] data) {
    var end = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 256)).IndexOf("ENDHDR\n", StringComparison.Ordinal);
    Assert.That(end, Is.GreaterThan(0), "Not a PAM file.");
    var fields = Encoding.ASCII.GetString(data, 0, end).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    int Field(string name) => int.Parse(Array.Find(fields, f => f.StartsWith(name + " ", StringComparison.Ordinal))![(name.Length + 1)..]);

    int width = Field("WIDTH"), height = Field("HEIGHT"), depth = Field("DEPTH"), maxValue = Field("MAXVAL");
    var body = data.AsSpan(end + 7);
    var samples = new ushort[width * height * depth];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = maxValue > 255 ? (ushort)(body[2 * i] << 8 | body[2 * i + 1]) : body[i];
    return (width, height, samples);
  }
}
