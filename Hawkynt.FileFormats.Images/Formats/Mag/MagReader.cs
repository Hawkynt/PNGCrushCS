using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Mag;

/// <summary>Reads MAKIchan Graphics files from bytes, streams, or file paths.</summary>
public static class MagReader {

  public static MagFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MAKIchan Graphics file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MagFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static MagFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= MagFile.Magic.Length)
      throw new InvalidDataException($"Data too small for a MAKIchan Graphics file (got {data.Length} bytes).");

    if (!data[..MagFile.Magic.Length].SequenceEqual(MagFile.Magic))
      throw new InvalidDataException("Not a MAKIchan Graphics file: it does not open with MAKI02.");

    // A comment of any length sits between the signature and the header, ending at the first 0x1A.
    var comment = data[MagFile.Magic.Length..].IndexOf((byte)0x1A);
    if (comment < 0)
      throw new InvalidDataException("A MAKIchan Graphics file ends its comment with 0x1A; this one has none.");

    var header = MagFile.Magic.Length + comment + 1;
    if (header + MagFile.HeaderSize > data.Length)
      throw new InvalidDataException("A MAKIchan Graphics header does not fit in this file.");

    var block = data[header..];
    var mode = block[3];
    var left = BinaryPrimitives.ReadUInt16LittleEndian(block[4..]);
    var top = BinaryPrimitives.ReadUInt16LittleEndian(block[6..]);
    var right = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
    var bottom = BinaryPrimitives.ReadUInt16LittleEndian(block[10..]);

    var storedWidth = right - left + 1;
    var storedHeight = bottom - top + 1;
    if (storedWidth <= 0 || storedHeight <= 0)
      throw new InvalidDataException($"A MAKIchan Graphics picture of {storedWidth}x{storedHeight} is no size.");

    var paletteCount = (mode & 0x80) != 0 ? 256 : 16;
    var bitsPerPixel = paletteCount == 256 ? 8 : 4;
    var bytesPerRow = storedWidth * bitsPerPixel / 8;
    if (bytesPerRow == 0 || bytesPerRow % 4 != 0)
      throw new InvalidDataException($"A MAKIchan Graphics row is a whole number of four-byte groups; {storedWidth} pixels at {bitsPerPixel} bits is not.");

    var paletteAt = header + MagFile.HeaderSize;
    if (paletteAt + paletteCount * 3 > data.Length)
      throw new InvalidDataException("A MAKIchan Graphics palette does not fit in this file.");

    // Green, then red, then blue. In 256 colours the bytes are whole and RECOIL renders them as they
    // stand; in sixteen only the top nibble carries anything and the files disagree about what to pad
    // it with — one writes 0x2F where another writes 0x20 for the same colour — so the nibble is
    // widened by repeating itself rather than trusted as a byte.
    var palette = new byte[paletteCount * 3];
    for (var i = 0; i < paletteCount; ++i) {
      palette[i * 3] = _Widen(data[paletteAt + i * 3 + 1], paletteCount);
      palette[i * 3 + 1] = _Widen(data[paletteAt + i * 3], paletteCount);
      palette[i * 3 + 2] = _Widen(data[paletteAt + i * 3 + 2], paletteCount);
    }

    var stored = _Unpack(data, header, block, bytesPerRow, storedHeight);

    // 256 colours are stored at half the horizontal resolution and shown doubled; the low bit of the
    // mode says the same of the height.
    var scaleX = paletteCount == 256 ? 2 : 1;
    var scaleY = (mode & 1) != 0 ? 2 : 1;

    return new() {
      Width = storedWidth * scaleX,
      Height = storedHeight * scaleY,
      PaletteCount = paletteCount,
      Palette = palette,
      PixelData = _ToIndices(stored, storedWidth, storedHeight, bytesPerRow, bitsPerPixel, scaleX, scaleY),
    };
  }

  /// <summary>Takes a palette byte as it stands, or as a nibble repeated where only a nibble is real.</summary>
  private static byte _Widen(byte value, int paletteCount)
    => paletteCount == 256 ? value : (byte)((value & 0xF0) | (value >> 4));

  /// <summary>
  /// Expands the two flag streams and the pixel stream into the stored picture.
  /// </summary>
  /// <remarks>
  /// Stream A carries one bit per four bytes of picture. Where a bit is set, one byte comes off stream
  /// B and is exclusive-ored into the running row of flags, which is why a row repeating the one above
  /// it costs no flag bytes at all. Each flag byte covers two two-byte units, high nibble first; a
  /// nibble of nought takes two bytes from the pixel stream and anything else copies two from earlier
  /// in the picture.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data, int header, ReadOnlySpan<byte> block, int bytesPerRow, int height) {
    var flagAAt = header + (int)BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
    var flagBAt = header + (int)BinaryPrimitives.ReadUInt32LittleEndian(block[16..]);
    var pixelsAt = header + (int)BinaryPrimitives.ReadUInt32LittleEndian(block[24..]);

    if (flagAAt < 0 || flagBAt < 0 || pixelsAt < 0)
      throw new InvalidDataException("A MAKIchan Graphics file states a stream before the start of itself.");

    var flagsPerRow = bytesPerRow / 4;
    var flags = new byte[flagsPerRow];
    var picture = new byte[bytesPerRow * height];

    var bit = 0;
    var flagB = 0;
    var pixel = 0;

    for (var y = 0; y < height; ++y) {
      for (var i = 0; i < flagsPerRow; ++i) {
        var at = flagAAt + (bit >> 3);
        if (at >= data.Length)
          throw new InvalidDataException("A MAKIchan Graphics flag stream ran out before the picture was whole.");

        if (((data[at] >> (7 - (bit & 7))) & 1) != 0) {
          if (flagBAt + flagB >= data.Length)
            throw new InvalidDataException("A MAKIchan Graphics flag stream ran out before the picture was whole.");

          flags[i] ^= data[flagBAt + flagB++];
        }

        ++bit;
      }

      var row = y * bytesPerRow;
      for (var i = 0; i < flagsPerRow; ++i)
        for (var half = 0; half < 2; ++half) {
          var code = half == 0 ? flags[i] >> 4 : flags[i] & 0x0F;
          var unit = i * 2 + half;
          var to = row + unit * 2;

          if (code == 0) {
            if (pixelsAt + pixel + 1 >= data.Length)
              throw new InvalidDataException("A MAKIchan Graphics pixel stream ran out before the picture was whole.");

            picture[to] = data[pixelsAt + pixel++];
            picture[to + 1] = data[pixelsAt + pixel++];
            continue;
          }

          var fromRow = y + MagFile.CopyRows[code];
          var fromUnit = unit + MagFile.CopyColumns[code];
          if (fromRow < 0 || fromUnit < 0)
            continue;

          var from = fromRow * bytesPerRow + fromUnit * 2;
          picture[to] = picture[from];
          picture[to + 1] = picture[from + 1];
        }
    }

    return picture;
  }

  /// <summary>Turns the stored picture into one index per displayed pixel.</summary>
  private static byte[] _ToIndices(byte[] stored, int width, int height, int bytesPerRow, int bitsPerPixel, int scaleX, int scaleY) {
    var indices = new byte[width * scaleX * height * scaleY];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = bitsPerPixel == 8
          ? stored[y * bytesPerRow + x]
          : (x & 1) == 0
            ? stored[y * bytesPerRow + x / 2] >> 4
            : stored[y * bytesPerRow + x / 2] & 0x0F;

        for (var dy = 0; dy < scaleY; ++dy)
          for (var dx = 0; dx < scaleX; ++dx)
            indices[(y * scaleY + dy) * width * scaleX + x * scaleX + dx] = (byte)value;
      }

    return indices;
  }

  public static MagFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
