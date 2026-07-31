using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxMig;

/// <summary>Reads MSX MIG pictures from bytes, streams, or file paths.</summary>
public static class MsxMigReader {

  /// <summary>Rows a screen record holds.</summary>
  private const int _ROWS = 212;

  public static MsxMigFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxMigFile FromStream(Stream stream) {
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

  public static MsxMigFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 16 || data[0] != 'M' || data[1] != 'S' || data[2] != 'X'
        || data[3] != 'M' || data[4] != 'I' || data[5] != 'G'
        || (data[6] | (data[7] << 8) | (data[8] << 16) | (data[9] << 24)) != data.Length - 6)
      throw new InvalidDataException("Not a MIG picture.");

    var unpacked = new byte[MsxMigFile.MaxUnpacked];
    var length = _Unpack(data, unpacked);

    var registers = new byte[256];
    var palette = new byte[16 * 3];
    var colors = 0;

    for (var at = 0; at < length;) {
      switch (unpacked[at]) {
        // A batch of register writes, each a register, a value and a mask of the bits it sets —
        // so a record can change part of a register without knowing the rest.
        case 0: {
          if (at + 1 >= length)
            throw new InvalidDataException("A register record has no count.");

          int count = unpacked[at + 1];
          if (at + 2 + count * 3 > length)
            throw new InvalidDataException("A register record runs past the records.");

          for (var i = 0; i < count; ++i) {
            var offset = at + 2 + i * 3;
            int register = unpacked[offset], mask = unpacked[offset + 2];
            registers[register] = (byte)((registers[register] & ~mask) | (unpacked[offset + 1] & mask));
          }

          at += 2 + count * 3;
          break;
        }

        case 1: {
          if (at + 2 >= length || unpacked[at + 1] != 0)
            throw new InvalidDataException("A palette record is malformed.");

          colors = unpacked[at + 2];
          if (at + 3 + (colors << 1) > length)
            throw new InvalidDataException("A palette record runs past the records.");

          palette = MsxGraphics.PaletteToRgb(unpacked.AsSpan(at + 3), Math.Min(colors, 16));
          at += 3 + (colors << 1);
          break;
        }

        case 2:
          return _Screen(unpacked, at, length, registers, palette, colors);

        default:
          throw new InvalidDataException($"Record {unpacked[at]} is not one MIG writes.");
      }
    }

    throw new InvalidDataException("A MIG picture has no screen.");
  }

  /// <summary>Reads the screen record and works out which mode the register writes put the chip in.</summary>
  private static MsxMigFile _Screen(
    byte[] unpacked, int at, int length, byte[] registers, byte[] palette, int colors) {
    if (at + 7 >= length || unpacked[at + 1] != 0 || unpacked[at + 2] != 0 || unpacked[at + 3] != 0
        || unpacked[at + 4] != 0 || unpacked[at + 6] != 0)
      throw new InvalidDataException("A screen record is malformed.");

    int pages = unpacked[at + 5];
    at += 7;

    // Two register bits say the chip was showing two pages alternately, and a second screen record
    // then follows the first.
    var interlaced = (registers[9] & 12) switch {
      0 => false,
      12 => true,
      _ => throw new InvalidDataException("A screen is neither one page nor two."),
    };

    if (!interlaced) {
      if (at + (pages << 8) + 1 != length)
        throw new InvalidDataException("A screen is not as long as its record says.");
    } else {
      var second = at + (pages << 8);
      if (second + (pages << 8) + 8 != length || unpacked[second] != 2 || unpacked[second + 1] != 0
          || unpacked[second + 4] != 0 || unpacked[second + 5] != pages || unpacked[second + 6] != 0)
        throw new InvalidDataException("An interlaced screen has no second page.");
    }

    // The chip's mode bits are spread across three registers; the page count completes the answer,
    // two of the modes differing only in how much memory they take.
    var mode = (registers[0] & 14) | ((registers[1] & 24) << 1) | ((registers[25] & 24) << 3) | (pages << 8);

    switch (mode) {
      case 14338:
        if (colors < 16 || interlaced)
          throw new InvalidDataException("Screen 4 is neither interlaced nor short of colours.");

        return _Sc4(unpacked, at, palette);

      case 1552:
        if (colors < 16 || interlaced)
          throw new InvalidDataException("Screen 3 is neither interlaced nor short of colours.");

        return _Sc3(unpacked, at, palette);
    }

    var (screen, needed) = mode switch {
      27142 => (5, 16),
      27144 => (6, 4),
      54282 => (7, 16),
      54286 => (8, 0),
      54478 => (10, 16),
      54350 => (12, 0),
      _ => throw new InvalidDataException($"Register state {mode} is not a graphics mode."),
    };

    if (colors < needed)
      throw new InvalidDataException($"Screen {screen} needs {needed} colours and the file names {colors}.");

    return _Bitmap(unpacked, at, screen, interlaced, screen == 8 ? _Screen8Palette() : palette, pages);
  }

  /// <summary>Screen 4: a character screen whose two colours change every row of every cell.</summary>
  private static MsxMigFile _Sc4(byte[] data, int at, byte[] palette) {
    var rgb = new byte[256 * 192 * 3];

    for (var y = 0; y < 192; ++y) {
      var font = at + ((y & 192) << 5) + (y & 7);

      for (var x = 0; x < 256; ++x) {
        var shape = font + (data[at + 6144 + ((y & ~7) << 2) + (x >> 3)] << 3);
        var attribute = data[8192 + shape];
        _Plot(rgb, (y * 256 + x) * 3, palette, ((data[shape] >> (~x & 7)) & 1) == 0 ? attribute & 15 : attribute >> 4);
      }
    }

    return new() { Width = 256, Height = 192, Pixels = rgb };
  }

  /// <summary>Screen 3: chunky blocks, which is the machine's lowest resolution and oldest mode.</summary>
  private static MsxMigFile _Sc3(byte[] data, int at, byte[] palette) {
    var rgb = new byte[256 * 192 * 3];

    for (var y = 0; y < 192; ++y)
    for (var x = 0; x < 256; ++x) {
      var cell = (y & 224) + (x >> 3);
      _Plot(rgb, (y * 256 + x) * 3, palette, (data[at + (cell << 3) + ((y >> 2) & 7)] >> (~x & 4)) & 15);
    }

    return new() { Width = 256, Height = 192, Pixels = rgb };
  }

  /// <summary>
  /// The bitmap modes, which differ only in how a pixel is found in memory and what it means.
  /// </summary>
  /// <remarks>
  /// Two of them are twice as wide as the others and are drawn at 512 across with every row shown
  /// twice; interlacing makes any of them 512 across, the two pages taking alternate rows.
  /// </remarks>
  private static MsxMigFile _Bitmap(
    byte[] data, int at, int mode, bool interlaced, byte[] palette, int pages) {
    var wide = mode >> 1 == 3;
    var width = interlaced || wide ? 512 : 256;
    var height = interlaced || wide ? _ROWS << 1 : _ROWS;
    var mask = interlaced ? 1 : 0;

    // The second page follows the first with a record header of its own between them.
    var second = at + (pages << 8) + 7;
    var rgb = new byte[width * height * 3];
    var group = new byte[4 * 3];

    for (var y = 0; y < height; ++y) {
      var page = (y & mask) == 0 ? at : second;

      for (var x = 0; x < width; ++x) {
        var target = (y * width + x) * 3;

        switch (mode) {
          case 5:
            _Plot(rgb, target, palette, MsxGraphics.GetNibble(data, page + ((y >> mask) << 7), x >> mask));
            break;

          case 6:
            _Plot(rgb, target, palette,
              (data[page + ((y >> 1) << 7) + (x >> 2)] >> ((~x & 3) << 1)) & 3);
            break;

          case 7:
            _Plot(rgb, target, palette, MsxGraphics.GetNibble(data, page + ((y >> 1) << 8), x));
            break;

          case 8:
            _Plot(rgb, target, palette, data[page + ((y >> mask) << 8) + (x >> mask)]);
            break;

          default: {
            // The two YJK modes. A group of four pixels shares its colour, so the group is decoded
            // whole and the wanted pixel taken out of it.
            var row = page + ((y >> mask) << 8);
            var source = x >> mask;
            MsxGraphics.DecodeYjkRow(data.AsSpan(row + (source & ~3), 4), 4, mode == 10, palette, group);

            var offset = (source & 3) * 3;
            rgb[target] = group[offset];
            rgb[target + 1] = group[offset + 1];
            rgb[target + 2] = group[offset + 2];
            break;
          }
        }
      }
    }

    return new() { Width = width, Height = height, Pixels = rgb };
  }

  /// <summary>
  /// Screen 8's fixed palette: three bits of red and green but only two of blue, the eye being
  /// least able to tell blues apart. The four blue levels are not evenly spaced either.
  /// </summary>
  private static byte[] _Screen8Palette() {
    ReadOnlySpan<byte> blues = [0, 2, 4, 7];
    var palette = new byte[256 * 3];

    for (var c = 0; c < 256; ++c) {
      palette[c * 3] = ChannelScaling.Expand3((c >> 2) & 7);
      palette[c * 3 + 1] = ChannelScaling.Expand3((c >> 5) & 7);
      palette[c * 3 + 2] = ChannelScaling.Expand3(blues[c & 3]);
    }

    return palette;
  }

  private static void _Plot(Span<byte> rgb, int target, ReadOnlySpan<byte> palette, int index) {
    var entry = index * 3;
    if (entry + 2 >= palette.Length)
      return;

    rgb[target] = palette[entry];
    rgb[target + 1] = palette[entry + 1];
    rgb[target + 2] = palette[entry + 2];
  }

  /// <summary>
  /// Unpacks the records. A bit before each byte says whether the byte stands for itself or names
  /// a distance back into what has been written already.
  /// </summary>
  private static int _Unpack(ReadOnlySpan<byte> data, byte[] unpacked) {
    var stream = new _Bits(data, 15);
    var written = 0;

    while (written < MsxMigFile.MaxUnpacked) {
      var copy = stream.Bit();
      if (copy < 0)
        return written;

      var b = stream.Byte();
      if (b < 0)
        return written;

      if (copy == 0) {
        unpacked[written++] = (byte)b;
        continue;
      }

      // A distance beyond 128 spends four more bits, so near matches stay cheap.
      if (b >= 128) {
        var extra = stream.Bits(4);
        if (extra < 0)
          return written;

        b += (extra - 1) << 7;
      }

      var distance = b + 1;
      if (written - distance < 0)
        throw new InvalidDataException("A MIG match reaches before the start of the records.");

      // The length's width comes first, as that many ones before a zero, so a short match costs
      // almost nothing to describe and a long one a little more.
      var width = -1;
      int bit;
      do {
        bit = stream.Bit();
        if (bit < 0)
          return written;

        ++width;
      } while (bit != 0);

      var length = stream.Bits(width);
      if (length < 0)
        return written;

      // The widest form is not a match at all but the end of a block; the next one starts on a
      // byte boundary, four bytes on.
      if (width >= 16) {
        if (!stream.NextBlock())
          return written;

        continue;
      }

      length += (1 << width) + 1;
      if (written + length > MsxMigFile.MaxUnpacked)
        throw new InvalidDataException("A MIG picture unpacks to more than it may.");

      do {
        unpacked[written] = unpacked[written - distance];
        ++written;
      } while (--length > 0);
    }

    return written;
  }

  /// <summary>The bit stream, whose bits come off the top of each byte.</summary>
  private ref struct _Bits {

    private readonly ReadOnlySpan<byte> _data;
    private int _bits;
    private int _at;

    public _Bits(ReadOnlySpan<byte> data, int at) {
      this._data = data;
      this._at = at;
    }

    public int Bit() {
      if ((this._bits & 127) == 0) {
        if (this._at >= this._data.Length)
          return -1;

        this._bits = (this._data[this._at++] << 1) | 1;
      } else
        this._bits <<= 1;

      return (this._bits >> 8) & 1;
    }

    public int Byte() => this._at < this._data.Length ? this._data[this._at++] : -1;

    public int Bits(int count) {
      var result = 0;
      while (--count >= 0) {
        var bit = this.Bit();
        if (bit < 0)
          return -1;

        result = (result << 1) | bit;
      }

      return result;
    }

    /// <summary>Steps over a block's trailer and starts the next one on a byte boundary.</summary>
    public bool NextBlock() {
      this._at += 4;
      if (this._at >= this._data.Length)
        return false;

      this._bits = 0;

      return true;
    }
  }

  public static MsxMigFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
