using System;
using System.IO;

namespace FileFormat.DegasIcon;

/// <summary>Reads DEGAS Elite icons from bytes, streams, or file paths.</summary>
public static class DegasIconReader {

  public static DegasIconFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Icon not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DegasIconFile FromStream(Stream stream) {
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

  public static DegasIconFile FromSpan(ReadOnlySpan<byte> data) {
    var at = 0;

    var width = _Define(data, ref at, "ICON_W");
    var height = _Define(data, ref at, "ICON_H");
    if (width is <= 0 or >= 256 || height is <= 0 or >= 256)
      throw new InvalidDataException($"An icon of {width}x{height} is not one DEGAS Elite wrote.");

    var words = (width + 15) >> 4;
    var size = _Define(data, ref at, "ICONSIZE");
    if (size != words * height)
      throw new InvalidDataException($"An icon of {width}x{height} is {words * height} words, not {size}.");

    foreach (var token in (string[])["int", "image[ICONSIZE]", "=", "{"])
      _Expect(data, ref at, token);

    var bitmap = new byte[size * 2];
    for (var i = 0; ;) {
      var value = _Hex(data, ref at);
      bitmap[i * 2] = (byte)(value >> 8);
      bitmap[i * 2 + 1] = (byte)value;

      if (++i >= size)
        break;

      if (at >= data.Length || data[at++] != ',')
        throw new InvalidDataException($"Word {i} of the icon is not followed by a comma.");
    }

    _Expect(data, ref at, "};");

    return new() { Width = width, Height = height, Bitmap = bitmap };
  }

  /// <summary>Reads one <c>#define</c> of the given name and returns the hexadecimal value.</summary>
  private static int _Define(ReadOnlySpan<byte> data, ref int at, string name) {
    _Expect(data, ref at, "#define");
    _Expect(data, ref at, name);

    return _Hex(data, ref at);
  }

  /// <summary>Reads a literal after the whitespace or comments that must precede it.</summary>
  /// <remarks>
  /// Something has to separate the tokens, so a token that follows the previous one immediately is
  /// a parse failure rather than the same token — which also means the file cannot begin with
  /// <c>#define</c>, and in practice never does: the exporter writes a comment first.
  /// </remarks>
  private static void _Expect(ReadOnlySpan<byte> data, ref int at, string token) {
    if (!_SkipWhitespaceAndComments(data, ref at))
      throw new InvalidDataException($"Nothing separates '{token}' from what precedes it.");

    foreach (var c in token) {
      if (at >= data.Length || data[at++] != c)
        throw new InvalidDataException($"Expected '{token}'.");
    }
  }

  private static int _Hex(ReadOnlySpan<byte> data, ref int at) {
    _Expect(data, ref at, "0x");

    return _ParseInt(data, ref at, 16, 65535);
  }

  /// <summary>Reads digits until one that does not belong, which is left for the caller.</summary>
  private static int _ParseInt(ReadOnlySpan<byte> data, ref int at, int radix, int maxValue) {
    var value = _Digit(data, ref at);
    if (value < 0 || value >= radix)
      throw new InvalidDataException("A number has no digits.");

    while (value <= maxValue) {
      var digit = _Digit(data, ref at);
      if (digit < 0)
        return value;

      if (digit >= radix)
        throw new InvalidDataException($"'{(char)data[at - 1]}' is not a digit in base {radix}.");

      value = value * radix + digit;
    }

    throw new InvalidDataException($"A number is larger than {maxValue}.");
  }

  private static int _Digit(ReadOnlySpan<byte> data, ref int at) {
    if (at >= data.Length)
      return -1;

    var c = data[at++];
    if (c is >= (byte)'0' and <= (byte)'9')
      return c - '0';
    if (c is >= (byte)'A' and <= (byte)'F')
      return c - 'A' + 10;
    if (c is >= (byte)'a' and <= (byte)'f')
      return c - 'a' + 10;

    --at;

    return -1;
  }

  /// <summary>Skips runs of space and C comments; reports whether anything was there to skip.</summary>
  private static bool _SkipWhitespaceAndComments(ReadOnlySpan<byte> data, ref int at) {
    var skipped = false;

    while (at < data.Length) {
      switch (data[at]) {
        case (byte)' ':
        case (byte)'\t':
        case (byte)'\r':
        case (byte)'\n':
          ++at;
          skipped = true;
          break;

        case (byte)'/':
          if (at >= data.Length - 3 || data[at + 1] != '*')
            return false;

          at += 3;
          do {
            if (++at > data.Length)
              return false;
          } while (data[at - 2] != '*' || data[at - 1] != '/');

          skipped = true;
          break;

        default:
          return skipped;
      }
    }

    return true;
  }

  public static DegasIconFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
