using System;
using System.IO;

namespace FileFormat.PcPaint;

/// <summary>Assembles PC Paint/Pictor Page Format file bytes from a PcPaintFile model.</summary>
public static class PcPaintWriter {

  public static byte[] ToBytes(PcPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Magic 0x1234 (LE: 34 12)
    ms.WriteByte((byte)(PcPaintFile.Magic & 0xFF));
    ms.WriteByte((byte)((PcPaintFile.Magic >> 8) & 0xFF));

    // Width (uint16 LE)
    _WriteUInt16(ms, (ushort)file.Width);

    // Height (uint16 LE)
    _WriteUInt16(ms, (ushort)file.Height);

    // X offset (uint16 LE)
    _WriteUInt16(ms, file.XOffset);

    // Y offset (uint16 LE)
    _WriteUInt16(ms, file.YOffset);

    // Planes (byte)
    ms.WriteByte(file.Planes);

    // Bits per pixel (byte)
    ms.WriteByte(file.BitsPerPixel);

    // X aspect (uint16 LE)
    _WriteUInt16(ms, file.XAspect);

    // Y aspect (uint16 LE)
    _WriteUInt16(ms, file.YAspect);

    // Palette info length (uint16 LE)
    var palette = file.Palette;
    var paletteLength = palette.Length > 0 ? PcPaintFile.PaletteSize : 0;
    _WriteUInt16(ms, (ushort)paletteLength);

    // Palette data
    if (paletteLength > 0) {
      if (palette.Length < PcPaintFile.PaletteSize) {
        var padded = new byte[PcPaintFile.PaletteSize];
        palette.AsSpan(0, Math.Min(palette.Length, PcPaintFile.PaletteSize)).CopyTo(padded);
        palette = padded;
      }

      ms.Write(palette, 0, PcPaintFile.PaletteSize);
    }

    // RLE-encoded pixel data
    var compressed = _CompressRle(file.PixelData);
    ms.Write(compressed, 0, compressed.Length);

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

      while (i < data.Length && data[i] == value)
        ++i;

      var count = i - runStart;

      while (count > 0) {
        if (count <= 255) {
          ms.WriteByte((byte)count);
          ms.WriteByte(value);
          count = 0;
        } else {
          var chunk = Math.Min(count, 65535);
          ms.WriteByte(0);
          ms.WriteByte(value);
          ms.WriteByte((byte)(chunk & 0xFF));
          ms.WriteByte((byte)((chunk >> 8) & 0xFF));
          count -= chunk;
        }
      }
    }

    return ms.ToArray();
  }

  private static void _WriteUInt16(Stream stream, ushort value) {
    stream.WriteByte((byte)(value & 0xFF));
    stream.WriteByte((byte)((value >> 8) & 0xFF));
  }
}
