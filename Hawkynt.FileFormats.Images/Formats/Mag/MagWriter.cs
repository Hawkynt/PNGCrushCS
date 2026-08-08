using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Mag;

/// <summary>Assembles MAKIchan Graphics bytes from a <see cref="MagFile"/>.</summary>
/// <remarks>
/// The compression is the reader's own, run backwards. Each two-byte unit is offered the fifteen
/// places a copy may come from, in the order the table lists them, and takes the first that already
/// holds what it wants; a unit no copy reaches goes into the pixel stream instead. The row of flags
/// persists, so a row identical to one the codes can reach costs nothing at all beyond its bits.
/// </remarks>
public static class MagWriter {

  /// <summary>The comment terminator, which is where the header begins.</summary>
  private const byte _COMMENT_END = 0x1A;

  public static byte[] ToBytes(MagFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var storedWidth = file.Width;
    var height = file.Height;
    var paletteCount = file.PaletteCount;
    var bitsPerPixel = paletteCount == 256 ? 8 : 4;
    var bytesPerRow = storedWidth * bitsPerPixel / 8;

    var stored = _ToStoredRows(file, storedWidth, height, bytesPerRow, bitsPerPixel);
    var (flagA, flagB, pixels) = _Pack(stored, bytesPerRow, height);

    var data = new List<byte>(1024 + pixels.Count);
    data.AddRange(MagFile.Magic.ToArray());
    data.Add(_COMMENT_END);

    var header = data.Count;
    data.AddRange(new byte[MagFile.HeaderSize + paletteCount * 3]);

    var block = data.Count - header;
    var flagAAt = block;
    var flagBAt = flagAAt + flagA.Count;
    var pixelsAt = flagBAt + flagB.Count;

    data.AddRange(flagA);
    data.AddRange(flagB);
    data.AddRange(pixels);

    var bytes = data.ToArray();
    var at = header;

    bytes[at + 3] = (byte)(paletteCount == 256 ? 0x80 : 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 4), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 6), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 8), (ushort)(storedWidth - 1));
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 10), (ushort)(height - 1));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 12), (uint)flagAAt);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 16), (uint)flagBAt);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 20), (uint)flagB.Count);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 24), (uint)pixelsAt);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 28), (uint)pixels.Count);

    // Green, then red, then blue, and only the top nibble of each is real in sixteen colours.
    var palette = file.Palette ?? [];
    var paletteAt = at + MagFile.HeaderSize;
    for (var i = 0; i < paletteCount; ++i) {
      var entry = i * 3;
      if (entry + 2 >= palette.Length)
        break;

      bytes[paletteAt + entry] = _Narrow(palette[entry + 1], paletteCount);
      bytes[paletteAt + entry + 1] = _Narrow(palette[entry], paletteCount);
      bytes[paletteAt + entry + 2] = _Narrow(palette[entry + 2], paletteCount);
    }

    return bytes;
  }

  /// <summary>Stores a channel whole, or as the nibble the reader widens back out of it.</summary>
  private static byte _Narrow(byte value, int paletteCount)
    => paletteCount == 256 ? value : (byte)(value & 0xF0);

  /// <summary>Packs the displayed indices back into the rows the compression works on.</summary>
  private static byte[] _ToStoredRows(MagFile file, int width, int height, int bytesPerRow, int bitsPerPixel) {
    var indices = file.PixelData ?? [];
    var stored = new byte[bytesPerRow * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var value = indices[y * width + x];
      if (bitsPerPixel == 8) {
        stored[y * bytesPerRow + x] = value;
        continue;
      }

      var at = y * bytesPerRow + x / 2;
      stored[at] |= (byte)((x & 1) == 0 ? value << 4 : value & 0x0F);
    }

    return stored;
  }

  /// <summary>Builds the two flag streams and the pixel stream from the stored rows.</summary>
  private static (List<byte> FlagA, List<byte> FlagB, List<byte> Pixels) _Pack(
    byte[] stored, int bytesPerRow, int height) {
    var flagsPerRow = bytesPerRow / 4;
    var flags = new byte[flagsPerRow];
    var wanted = new byte[flagsPerRow];

    var flagA = new List<byte>();
    var flagB = new List<byte>();
    var pixels = new List<byte>();

    var bits = 0;
    var pending = 0;

    for (var y = 0; y < height; ++y) {
      Array.Clear(wanted);

      for (var unit = 0; unit < flagsPerRow * 2; ++unit) {
        var code = _ChooseCode(stored, bytesPerRow, y, unit);
        wanted[unit >> 1] |= (byte)((unit & 1) == 0 ? code << 4 : code);
      }

      for (var i = 0; i < flagsPerRow; ++i) {
        // A bit says the running flag byte changes, and the byte that follows is the difference.
        pending <<= 1;
        if (wanted[i] != flags[i]) {
          pending |= 1;
          flagB.Add((byte)(wanted[i] ^ flags[i]));
          flags[i] = wanted[i];
        }

        if (++bits != 8)
          continue;

        flagA.Add((byte)pending);
        pending = 0;
        bits = 0;
      }

      var row = y * bytesPerRow;
      for (var unit = 0; unit < flagsPerRow * 2; ++unit) {
        var code = (unit & 1) == 0 ? flags[unit >> 1] >> 4 : flags[unit >> 1] & 0x0F;
        if (code != 0)
          continue;

        pixels.Add(stored[row + unit * 2]);
        pixels.Add(stored[row + unit * 2 + 1]);
      }
    }

    if (bits > 0)
      flagA.Add((byte)(pending << (8 - bits)));

    return (flagA, flagB, pixels);
  }

  /// <summary>The first copy that already holds the unit's two bytes, or nought for a literal.</summary>
  private static int _ChooseCode(byte[] stored, int bytesPerRow, int y, int unit) {
    var to = y * bytesPerRow + unit * 2;

    for (var code = 1; code < MagFile.CopyColumns.Length; ++code) {
      var fromRow = y + MagFile.CopyRows[code];
      var fromUnit = unit + MagFile.CopyColumns[code];
      if (fromRow < 0 || fromUnit < 0)
        continue;

      var from = fromRow * bytesPerRow + fromUnit * 2;
      if (stored[from] == stored[to] && stored[from + 1] == stored[to + 1])
        return code;
    }

    return 0;
  }
}
