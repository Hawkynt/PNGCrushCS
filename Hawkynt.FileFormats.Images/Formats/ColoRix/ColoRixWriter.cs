using System;
using System.IO;

namespace FileFormat.ColoRix;

/// <summary>Assembles ColoRIX VGA paint file bytes from a ColoRixFile model.</summary>
public static class ColoRixWriter {

  public static byte[] ToBytes(ColoRixFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Magic "RIX3"
    ms.WriteByte((byte)'R');
    ms.WriteByte((byte)'I');
    ms.WriteByte((byte)'X');
    ms.WriteByte((byte)'3');

    // Width and height stored as value - 1 (LE)
    var storedWidth = (ushort)(file.Width - 1);
    var storedHeight = (ushort)(file.Height - 1);
    ms.WriteByte((byte)(storedWidth & 0xFF));
    ms.WriteByte((byte)((storedWidth >> 8) & 0xFF));
    ms.WriteByte((byte)(storedHeight & 0xFF));
    ms.WriteByte((byte)((storedHeight >> 8) & 0xFF));

    // Palette type (always VGA when palette is present)
    ms.WriteByte(ColoRixFile.VgaPaletteType);

    // Storage type
    ms.WriteByte((byte)file.StorageType);

    // VGA palette (768 bytes)
    var palette = file.Palette;
    if (palette.Length < ColoRixFile.PaletteSize) {
      var padded = new byte[ColoRixFile.PaletteSize];
      palette.AsSpan(0, Math.Min(palette.Length, ColoRixFile.PaletteSize)).CopyTo(padded);
      palette = padded;
    }

    ms.Write(palette, 0, ColoRixFile.PaletteSize);

    // Pixel data
    if (file.StorageType == ColoRixCompression.Rle) {
      var compressed = _CompressRle(file.PixelData);
      ms.Write(compressed, 0, compressed.Length);
    } else
      ms.Write(file.PixelData, 0, file.PixelData.Length);

    return ms.ToArray();
  }

  internal static byte[] _CompressRle(byte[] data) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < data.Length) {
      var value = data[i];
      var runStart = i;

      while (i < data.Length && i - runStart < 255 && data[i] == value)
        ++i;

      var count = i - runStart;
      ms.WriteByte((byte)count);
      ms.WriteByte(value);
    }

    return ms.ToArray();
  }
}
