using System;
using System.IO;
using System.Text;

namespace FileFormat.LuraDocument.Tests;

/// <summary>Loads the committed LuraDocument fixtures and the binary PNM pictures beside them.</summary>
internal static class LuraDocumentFixtures {

  public static byte[] Bytes(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "LuraDocument", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>Parses a binary PGM (P5), PPM (P6) or PBM (P4, kept packed).</summary>
  public static (int Width, int Height, int Channels, byte[] Pixels) Pnm(byte[] data) {
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
    if (magic != "P4")
      Token();
    ++position;
    var channels = magic == "P6" ? 3 : 1;
    return (width, height, channels, data[position..]);
  }

  /// <summary>A page's pixels as the plug-in hands them out: bilevel pages as 0 and 255.</summary>
  public static byte[] AsDecoded(LuraDocumentPage page) {
    if (page.Kind != LuraDocumentPageKind.Bilevel)
      return page.PixelData;
    var levels = new byte[page.PixelData.Length];
    for (var i = 0; i < levels.Length; ++i)
      levels[i] = (byte)(page.PixelData[i] * 255);
    return levels;
  }
}
