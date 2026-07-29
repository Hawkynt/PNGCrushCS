using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Netpbm;

/// <summary>Parses text-based Netpbm headers, skipping comments.</summary>
internal static class NetpbmHeaderParser {

  internal readonly struct ParsedHeader {
    public NetpbmFormat Format { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int MaxValue { get; init; }
    public int Channels { get; init; }
    public string? TupleType { get; init; }
    public int DataOffset { get; init; }
  }

  internal static ParsedHeader Parse(byte[] data) {
    var format = _ReadMagic(data);
    return format == NetpbmFormat.Pam ? _ParsePam(data) : _ParsePlain(data, format);
  }

  private static NetpbmFormat _ReadMagic(byte[] data) {
    if (data.Length < 2 || data[0] != (byte)'P')
      throw new InvalidDataException("Invalid Netpbm magic number.");

    return data[1] switch {
      (byte)'1' => NetpbmFormat.PbmAscii,
      (byte)'2' => NetpbmFormat.PgmAscii,
      (byte)'3' => NetpbmFormat.PpmAscii,
      (byte)'4' => NetpbmFormat.PbmBinary,
      (byte)'5' => NetpbmFormat.PgmBinary,
      (byte)'6' => NetpbmFormat.PpmBinary,
      (byte)'7' => NetpbmFormat.Pam,
      _ => throw new InvalidDataException($"Unknown Netpbm format: P{(char)data[1]}.")
    };
  }

  private static ParsedHeader _ParsePlain(byte[] data, NetpbmFormat format) {
    var offset = 2; // skip "Px"
    var tokens = new List<string>();
    var needed = format is NetpbmFormat.PbmAscii or NetpbmFormat.PbmBinary ? 2 : 3;

    while (tokens.Count < needed && offset < data.Length) {
      _SkipWhitespaceAndComments(data, ref offset);
      if (offset >= data.Length)
        break;

      var token = _ReadToken(data, ref offset);
      if (token.Length > 0)
        tokens.Add(token);
    }

    if (tokens.Count < needed)
      throw new InvalidDataException("Incomplete Netpbm header.");

    var width = int.Parse(tokens[0]);
    var height = int.Parse(tokens[1]);
    var maxValue = needed == 3 ? int.Parse(tokens[2]) : 1;

    var channels = format switch {
      NetpbmFormat.PbmAscii or NetpbmFormat.PbmBinary => 1,
      NetpbmFormat.PgmAscii or NetpbmFormat.PgmBinary => 1,
      NetpbmFormat.PpmAscii or NetpbmFormat.PpmBinary => 3,
      _ => 1
    };

    // For binary formats, skip exactly one whitespace character after last header token
    if (format is NetpbmFormat.PbmBinary or NetpbmFormat.PgmBinary or NetpbmFormat.PpmBinary) {
      if (offset < data.Length && _IsWhitespace(data[offset]))
        ++offset;
    }

    return new ParsedHeader {
      Format = format,
      Width = width,
      Height = height,
      MaxValue = maxValue,
      Channels = channels,
      TupleType = null,
      DataOffset = offset
    };
  }

  private static ParsedHeader _ParsePam(byte[] data) {
    var offset = 2; // skip "P7"
    int width = 0, height = 0, depth = 0, maxval = 0;
    string? tupleType = null;

    // Skip the whitespace/newline after "P7"
    if (offset < data.Length && _IsWhitespace(data[offset]))
      ++offset;

    while (offset < data.Length) {
      _SkipWhitespaceAndComments(data, ref offset);
      if (offset >= data.Length)
        break;

      var line = _ReadLine(data, ref offset);
      if (line == "ENDHDR")
        break;

      var spaceIdx = line.IndexOf(' ');
      if (spaceIdx < 0)
        continue;

      var keyword = line[..spaceIdx];
      var value = line[(spaceIdx + 1)..].Trim();

      switch (keyword) {
        case "WIDTH":
          width = int.Parse(value);
          break;
        case "HEIGHT":
          height = int.Parse(value);
          break;
        case "DEPTH":
          depth = int.Parse(value);
          break;
        case "MAXVAL":
          maxval = int.Parse(value);
          break;
        case "TUPLTYPE":
          tupleType = value;
          break;
      }
    }

    if (width <= 0 || height <= 0 || depth <= 0 || maxval <= 0)
      throw new InvalidDataException("Incomplete PAM header.");

    return new ParsedHeader {
      Format = NetpbmFormat.Pam,
      Width = width,
      Height = height,
      MaxValue = maxval,
      Channels = depth,
      TupleType = tupleType,
      DataOffset = offset
    };
  }

  private static void _SkipWhitespaceAndComments(byte[] data, ref int offset) {
    while (offset < data.Length) {
      if (data[offset] == (byte)'#') {
        while (offset < data.Length && data[offset] != (byte)'\n')
          ++offset;

        if (offset < data.Length)
          ++offset; // skip the newline
      } else if (_IsWhitespace(data[offset]))
        ++offset;
      else
        break;
    }
  }

  private static string _ReadToken(byte[] data, ref int offset) {
    var start = offset;
    while (offset < data.Length && !_IsWhitespace(data[offset]) && data[offset] != (byte)'#')
      ++offset;

    return Encoding.ASCII.GetString(data, start, offset - start);
  }

  private static string _ReadLine(byte[] data, ref int offset) {
    var start = offset;
    while (offset < data.Length && data[offset] != (byte)'\n' && data[offset] != (byte)'\r')
      ++offset;

    var line = Encoding.ASCII.GetString(data, start, offset - start);

    // skip line ending
    if (offset < data.Length && data[offset] == (byte)'\r')
      ++offset;

    if (offset < data.Length && data[offset] == (byte)'\n')
      ++offset;

    return line;
  }

  private static bool _IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';
}
