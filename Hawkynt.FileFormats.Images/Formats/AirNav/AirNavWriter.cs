using System;
using System.IO;

namespace FileFormat.AirNav;

/// <summary>Writes AirNav pictures (.anv).</summary>
/// <remarks>
/// What goes out is the 256-colour Windows bitmap the format is, with <c>AN</c> where the two letters
/// go: the file header, a forty-byte information header, the colour table at 54 and the rows from
/// the bottom up at 1078. Every field the reader checks is written to agree with the picture, so the
/// file is the one XnView reads and not only the one this reads back.
/// </remarks>
public static class AirNavWriter {

  public static byte[] ToBytes(AirNavFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture to write.");
    if (file.Width is < 1 or > AirNavFile.MaximumSide || file.Height is < 1 or > AirNavFile.MaximumSide)
      throw new InvalidOperationException($"An AirNav picture of {file.Width}x{file.Height} cannot be written.");
    if (file.PixelData.Length != (long)file.Width * file.Height)
      throw new InvalidOperationException($"A {file.Width}x{file.Height} picture needs {(long)file.Width * file.Height} indices and {file.PixelData.Length} were given.");

    var stride = (file.Width + 3) & ~3;
    var output = new byte[AirNavFile.PixelOffset + stride * file.Height];
    AirNavFile.Magic.CopyTo(output);
    _Write32(output, 2, output.Length);
    _Write32(output, 10, AirNavFile.PixelOffset);
    _Write32(output, 14, 40);
    _Write32(output, 18, file.Width);
    _Write32(output, 22, file.Height);
    output[26] = 1;
    output[28] = 8;
    _Write32(output, 34, stride * file.Height);
    _Write32(output, 46, AirNavFile.PaletteEntries);
    _Write32(output, 50, AirNavFile.PaletteEntries);

    var palette = file.Palette ?? [];
    for (var i = 0; i < AirNavFile.PaletteEntries; ++i) {
      var at = AirNavFile.PaletteOffset + i * 4;
      var from = i * 3;
      if (from + 2 >= palette.Length)
        continue;

      output[at] = palette[from + 2];
      output[at + 1] = palette[from + 1];
      output[at + 2] = palette[from];
    }

    for (var y = 0; y < file.Height; ++y) {
      var target = AirNavFile.PixelOffset + (file.Height - 1 - y) * stride;
      Array.Copy(file.PixelData, y * file.Width, output, target, file.Width);
    }

    return output;
  }

  public static void ToStream(AirNavFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(AirNavFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }

  private static void _Write32(byte[] data, int at, int value) {
    data[at] = (byte)value;
    data[at + 1] = (byte)(value >> 8);
    data[at + 2] = (byte)(value >> 16);
    data[at + 3] = (byte)(value >> 24);
  }
}
