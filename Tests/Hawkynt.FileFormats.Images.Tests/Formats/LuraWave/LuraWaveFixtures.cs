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
}
