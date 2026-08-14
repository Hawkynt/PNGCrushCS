using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Graphics10Plus;

/// <summary>Serialises an Atari 8-bit Graphics 10+ screen: the bitmap, then the nine registers.</summary>
public static class Graphics10PlusWriter {

  public static byte[] ToBytes(Graphics10PlusFile file) {
    var result = new byte[Graphics10PlusFile.FileSize];

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, Graphics10PlusFile.ScreenDataSize))
      .CopyTo(result.AsSpan(0, Graphics10PlusFile.ScreenDataSize));

    var registers = file.Registers ?? [];
    registers.AsSpan(0, Math.Min(registers.Length, Graphics10PlusFile.RegisterCount))
      .CopyTo(result.AsSpan(Graphics10PlusFile.RegisterOffset, Graphics10PlusFile.RegisterCount));

    return result;
  }

  /// <summary>Packs a sixty-row picture into the bitmap, each pixel naming the register nearest it.</summary>
  /// <param name="rgb">The picture at its stored size, eighty by sixty.</param>
  /// <param name="registers">PM0 to PM3, PF0 to PF3, then the background.</param>
  /// <remarks>
  /// Only the first nine of the sixteen values a nibble can hold are written. The other seven are
  /// aliases of these, so using them would change nothing on the screen and would leave a file whose
  /// pixels name registers by their second name for no reason.
  /// </remarks>
  internal static byte[] Pack(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers) {
    var gtia = Atari8BitGraphics.Palette;
    var data = new byte[Graphics10PlusFile.ScreenDataSize];

    for (var y = 0; y < Graphics10PlusFile.ScreenRows; ++y)
    for (var x = 0; x < Graphics10PlusFile.StoredWidth; ++x) {
      var source = (y * Graphics10PlusFile.StoredWidth + x) * 3;
      if (source + 2 >= rgb.Length)
        break;

      var best = 0;
      var bestCost = int.MaxValue;
      for (var candidate = 0; candidate < Graphics10PlusFile.RegisterCount && candidate < registers.Length; ++candidate) {
        // The chip drops the low bit of a register, so the colour to measure against is the one it
        // would actually show rather than the one the byte states.
        var entry = (registers[candidate] & 0xFE) * 3;
        int dr = rgb[source] - gtia[entry], dg = rgb[source + 1] - gtia[entry + 1], db = rgb[source + 2] - gtia[entry + 2];
        var cost = dr * dr + dg * dg + db * db;
        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = candidate;
      }

      // High nibble first, which is the pixel on the left.
      data[y * Graphics10PlusFile.BytesPerRow + (x >> 1)] |= (byte)(best << ((x & 1) == 0 ? 4 : 0));
    }

    return data;
  }

  public static void ToFile(Graphics10PlusFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
