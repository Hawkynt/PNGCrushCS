using System;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Rpl;

/// <summary>
/// The twenty-one newline-terminated text fields every ARMovie/RPL file opens with — the format early
/// PC Tomb Raider games and Eidos' other Escape-codec titles use, with no relation to RIFF at all.
/// </summary>
/// <remarks>
/// Each line is a decimal value followed by whitespace and a human-readable description of what the
/// value means — <c>"130        video format"</c> — so a field is read by taking the token before the
/// first run of whitespace and discarding the rest of the line. The layout, field order and the "number
/// of chunks names the highest index rather than a count" quirk below are recorded against real bytes
/// in this package's <c>codec-investigations.md</c>, from the Escape 124 investigation that mapped this
/// container before any codec riding on it was implemented.
/// </remarks>
public readonly record struct RplHeader(
  string MovieName,
  string Copyright,
  string AuthorTool,
  int VideoCompressionFormat,
  int Width,
  int Height,
  int PixelDepth,
  Rational FrameRate,
  int SoundCompressionFormat,
  int SampleRate,
  int ChannelCount,
  int SamplePrecision,
  int FramesPerChunk,
  int HighestChunkIndex,
  long ChunkCatalogueOffset,
  long HeaderByteLength) {

  private const int _FieldCount = 21;

  internal static RplHeader Parse(ReadOnlySpan<byte> data) {
    Span<Range> lines = stackalloc Range[_FieldCount];
    var at = 0;
    for (var i = 0; i < _FieldCount; ++i) {
      var newline = data[at..].IndexOf((byte)'\n');
      if (newline < 0)
        throw new InvalidDataException(
          $"An ARMovie/RPL header is twenty-one newline-terminated lines; this file ran out of data "
          + $"after {i} of them.");

      lines[i] = at..(at + newline);
      at += newline + 1;
    }

    if (!_Token(data, lines[0]).SequenceEqual("ARMovie"u8))
      throw new NotSupportedException("The file does not open with the \"ARMovie\" signature on its own first line.");

    return new(
      MovieName: _Line(data, lines[1]),
      Copyright: _Line(data, lines[2]),
      AuthorTool: _Line(data, lines[3]),
      VideoCompressionFormat: _IntToken(data, lines[4]),
      Width: _IntToken(data, lines[5]),
      Height: _IntToken(data, lines[6]),
      PixelDepth: _IntToken(data, lines[7]),
      FrameRate: _RationalToken(data, lines[8]),
      SoundCompressionFormat: _IntToken(data, lines[9]),
      SampleRate: _IntToken(data, lines[10]),
      ChannelCount: _IntToken(data, lines[11]),
      SamplePrecision: _IntToken(data, lines[12]),
      FramesPerChunk: _IntToken(data, lines[13]),
      HighestChunkIndex: _IntToken(data, lines[14]),
      // Fields 15 and 16 (even/odd chunk size) are a fixed-chunk-size hint this reader does not need:
      // the catalogue below states every chunk's real offset and size regardless of whether the file
      // ever used a fixed size at all. Field 17 is the one this reader walks from.
      ChunkCatalogueOffset: _LongToken(data, lines[17]),
      HeaderByteLength: at);
  }

  private static ReadOnlySpan<byte> _Token(ReadOnlySpan<byte> data, Range line) {
    var span = data[line];
    var spaceAt = span.IndexOf((byte)' ');
    return spaceAt < 0 ? span : span[..spaceAt];
  }

  private static string _Line(ReadOnlySpan<byte> data, Range line) => Encoding.Latin1.GetString(data[line]).TrimEnd('\r');

  private static int _IntToken(ReadOnlySpan<byte> data, Range line) {
    var token = _Token(data, line);
    if (!Utf8Parser.TryParseInt(token, out var value))
      throw new InvalidDataException($"An ARMovie/RPL header field is not a decimal integer: \"{Encoding.Latin1.GetString(token)}\".");

    return value;
  }

  private static long _LongToken(ReadOnlySpan<byte> data, Range line) {
    var token = _Token(data, line);
    if (!Utf8Parser.TryParseLong(token, out var value))
      throw new InvalidDataException($"An ARMovie/RPL header field is not a decimal integer: \"{Encoding.Latin1.GetString(token)}\".");

    return value;
  }

  /// <summary>Reads a decimal fraction such as <c>30.000000</c> as an exact ratio rather than through
  /// a <see cref="double"/>, since a frame rate is a timing fact and not a display number.</summary>
  private static Rational _RationalToken(ReadOnlySpan<byte> data, Range line) {
    var token = Encoding.Latin1.GetString(_Token(data, line));
    var dot = token.IndexOf('.');
    if (dot < 0)
      return long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
        ? new(whole, 1)
        : Rational.Unknown;

    var wholePart = token[..dot];
    var fractionPart = token[(dot + 1)..].TrimEnd('0');
    if (fractionPart.Length == 0)
      return long.TryParse(wholePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
        ? new(w, 1)
        : Rational.Unknown;

    if (!long.TryParse(wholePart + fractionPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator))
      return Rational.Unknown;

    long denominator = 1;
    for (var i = 0; i < fractionPart.Length; ++i)
      denominator *= 10;

    return new(numerator, denominator);
  }
}

/// <summary>A tiny ASCII-decimal parser over spans, since the header's fields are plain digits and
/// nothing here wants to allocate a string just to call <see cref="int.TryParse(string)"/>.</summary>
internal static class Utf8Parser {
  internal static bool TryParseInt(ReadOnlySpan<byte> token, out int value) {
    if (!TryParseLong(token, out var longValue) || longValue is < int.MinValue or > int.MaxValue) {
      value = 0;
      return false;
    }

    value = (int)longValue;
    return true;
  }

  internal static bool TryParseLong(ReadOnlySpan<byte> token, out long value) {
    value = 0;
    if (token.Length == 0)
      return false;

    var negative = token[0] == (byte)'-';
    var start = negative ? 1 : 0;
    if (start >= token.Length)
      return false;

    long result = 0;
    for (var i = start; i < token.Length; ++i) {
      var b = token[i];
      if (b is < (byte)'0' or > (byte)'9')
        return false;

      result = (result * 10) + (b - (byte)'0');
    }

    value = negative ? -result : result;
    return true;
  }
}
