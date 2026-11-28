using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileFormat.Pwc.Tests;

/// <summary>The pwcencode/pwcdecode fixture pairs, and an independent reading of pwcdecode's BMP output.</summary>
internal static class PwcFixtures {

  public static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pwc");

  public static IEnumerable<string> Names() {
    return System.IO.Directory.GetFiles(Directory, "*.pwc")
      .Select(static path => Path.GetFileNameWithoutExtension(path))
      .OrderBy(static name => name, StringComparer.Ordinal);
  }

  public static byte[] Pwc(string name) => File.ReadAllBytes(Path.Combine(Directory, name + ".pwc"));

  public static Bitmap Bmp(string name) => ReadBmp(File.ReadAllBytes(Path.Combine(Directory, name + ".bmp")));

  /// <summary>Where the arithmetic-coded stream starts: after header, palette and option word.</summary>
  public static int PayloadOffset(byte[] pwc) {
    var offset = 12;
    if ((pwc[9] & 1) != 0)
      offset += 2 + 3 * (pwc[12] | pwc[13] << 8);

    return offset + 4;
  }

  /// <summary>A palette picture as pwcdecode wrote it: one index per pixel, top row first, palette as RGB.</summary>
  public sealed record Bitmap(int Width, int Height, int BitsPerPixel, byte[] Indices, byte[] Palette);

  /// <summary>Reads the uncompressed 1/4/8-bit BMPs pwcdecode writes, without going through this package's BMP reader.</summary>
  public static Bitmap ReadBmp(byte[] bmp) {
    var offset = BitConverter.ToInt32(bmp, 10);
    var headerSize = BitConverter.ToInt32(bmp, 14);
    var width = BitConverter.ToInt32(bmp, 18);
    var height = BitConverter.ToInt32(bmp, 22);
    var bpp = BitConverter.ToInt16(bmp, 28);
    var colorsUsed = BitConverter.ToInt32(bmp, 46);
    var entries = colorsUsed != 0 ? colorsUsed : 1 << bpp;
    var palette = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var at = 14 + headerSize + i * 4;
      palette[i * 3] = bmp[at + 2];
      palette[i * 3 + 1] = bmp[at + 1];
      palette[i * 3 + 2] = bmp[at];
    }

    var rows = Math.Abs(height);
    var stride = (width * bpp + 31) / 32 * 4;
    var indices = new byte[width * rows];
    for (var y = 0; y < rows; ++y) {
      var row = offset + (height > 0 ? rows - 1 - y : y) * stride;
      for (var x = 0; x < width; ++x) {
        var value = bmp[row + x * bpp / 8];
        indices[y * width + x] = bpp switch {
          8 => value,
          4 => (byte)((x & 1) == 0 ? value >> 4 : value & 15),
          _ => (byte)((value >> (7 - (x & 7))) & 1),
        };
      }
    }

    return new(width, rows, bpp, indices, palette);
  }
}
