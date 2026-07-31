using System;
using System.IO;

namespace FileFormat.MapletownNl3;

/// <summary>Reads Mapletown Network NL3 pictures from bytes, streams, or file paths.</summary>
public static class MapletownNl3Reader {

  /// <summary>Levels each channel of a palette entry can take.</summary>
  private const int _LEVELS = 9;

  /// <summary>Colours a palette entry can name: nine levels in each of three channels.</summary>
  private const int _COLOR_SPACE = _LEVELS * _LEVELS * _LEVELS;

  public static MapletownNl3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MapletownNl3File FromStream(Stream stream) {
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

  public static MapletownNl3File FromSpan(ReadOnlySpan<byte> data) {
    var at = 0;
    var palette = new byte[MapletownNl3File.ColorCount * 3];

    for (var i = 0; i < MapletownNl3File.ColorCount; ++i) {
      // Two characters a colour: seven bits in the first and the rest in the second, because no
      // single printable character can carry the 729 values a colour needs.
      var low = _Value(data, ref at);
      if (low is < 0 or > 127)
        throw new InvalidDataException($"Palette entry {i} has no low half.");

      var color = low | (_Value(data, ref at) << 7);
      if (color is < 0 or >= _COLOR_SPACE)
        throw new InvalidDataException($"Palette entry {i} names colour {color}.");

      // Nine levels a channel, spread over a byte so that the top level is white.
      palette[i * 3] = (byte)(color / (_LEVELS * _LEVELS) * 255 >> 3);
      palette[i * 3 + 1] = (byte)(color / _LEVELS % _LEVELS * 255 >> 3);
      palette[i * 3 + 2] = (byte)(color % _LEVELS * 255 >> 3);
    }

    var pixels = new byte[MapletownNl3File.Width * MapletownNl3File.Height];
    var run = 0;
    var value = 0;

    // Column by column, which is how the terminal drew it.
    for (var x = 0; x < MapletownNl3File.Width; ++x)
    for (var y = 0; y < MapletownNl3File.Height; ++y) {
      while (run == 0) {
        var command = _Value(data, ref at);
        if (command is < 0 or > 127)
          throw new InvalidDataException("A picture ends before its pixels do.");

        // Six bits are the colour and the seventh says whether a length follows.
        value = command & 63;
        if (command < 64) {
          run = 1;
          continue;
        }

        var length = _Value(data, ref at);
        if (length < 0)
          throw new InvalidDataException("A run has no length.");

        // A run is never shorter than two; a single pixel is written the short way.
        run = length + 2;
      }

      --run;
      pixels[y * MapletownNl3File.Width + x] = (byte)value;
    }

    return new() { Pixels = pixels, Palette = palette };
  }

  /// <summary>Reads one value, which is one printable character or a sequence standing for one.</summary>
  private static int _Value(ReadOnlySpan<byte> data, ref int at) {
    var c = _Character(data, ref at);

    return c switch {
      // The printable ASCII range, less its first character, is the bulk of the alphabet.
      >= 32 and < 127 => c - 32,

      // The half-width Japanese characters continue it, the gap between being the codes a board
      // would have eaten.
      >= 160 and < 224 => c - 65,
      253 => 159,
      254 => 160,
      _ => -1,
    };
  }

  /// <summary>
  /// Reads one character, skipping line breaks and decoding the three-byte sequences that stand
  /// for characters a plain byte could not carry.
  /// </summary>
  private static int _Character(ReadOnlySpan<byte> data, ref int at) {
    int c;
    do {
      if (at >= data.Length)
        return -1;

      c = data[at++];
    } while (c is '\r' or '\n');

    if (c != 0xEF)
      return c;

    if (at + 1 >= data.Length)
      return -1;

    switch (data[at++]) {
      case 0xBD: {
        var next = data[at++];
        return next is >= 160 and <= 191 ? next : -1;
      }

      case 0xBE: {
        var next = data[at++];
        return next is >= 128 and <= 159 ? next + 64 : -1;
      }

      default:
        return -1;
    }
  }

  public static MapletownNl3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
