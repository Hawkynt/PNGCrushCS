using System;
using System.IO;

namespace FileFormat.Mtv;

/// <summary>Reads MTV/PRT ray-tracer files from bytes, streams, or file paths.</summary>
public static class MtvReader {

  private const int _MaximumHeaderLength = 128;

  public static MtvFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MTV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MtvFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static MtvFile FromSpan(ReadOnlySpan<byte> data) {
    if (!TryReadHeader(data, out var width, out var height, out var pixelOffset))
      throw new InvalidDataException("Invalid MTV header; expected one 'width height' ASCII line with positive dimensions.");

    var expectedPixelBytes = checked(width * height * 3);
    var available = data.Length - pixelOffset;

    // nconvert puts one 0x00 between the size line and the samples and will not read a file back
    // without it, though neither Rayshade nor the MTV tracer itself writes one. It is taken as
    // padding only when the payload is otherwise one byte too long, so a genuinely black first
    // pixel in an exactly-sized file stays a sample.
    if (available == expectedPixelBytes + 1 && data[pixelOffset] == 0) {
      ++pixelOffset;
      --available;
    }

    // The historical PBMPLUS converter reads exactly the stated raster and stops. Match that
    // behavior: truncation is invalid, while bytes after a complete raster are not part of it.
    if (available < expectedPixelBytes)
      throw new InvalidDataException($"MTV payload holds {available} bytes but {width}x{height} needs {expectedPixelBytes}.");

    return new() {
      Width = width,
      Height = height,
      PixelData = data.Slice(pixelOffset, expectedPixelBytes).ToArray(),
    };
  }

  public static MtvFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  internal static bool? MatchesSignature(ReadOnlySpan<byte> data) {
    var lineEnd = data.IndexOf((byte)'\n');
    if (lineEnd < 0)
      return data.Length < _MaximumHeaderLength ? null : false;

    if (!TryReadHeader(data, out var width, out var height, out var pixelOffset))
      return false;

    var expectedPixelBytes = checked(width * height * 3);
    var available = data.Length - pixelOffset;
    if (available < expectedPixelBytes)
      return null;

    // This format has no magic bytes. Only claim a structural match when the inspected buffer
    // contains one complete canonical raster (or nconvert's known one-byte pad), rather than
    // classifying arbitrary text beginning with two integers as MTV.
    if (available == expectedPixelBytes)
      return true;
    if (available == expectedPixelBytes + 1 && data[pixelOffset] == 0)
      return true;

    return false;
  }

  internal static bool TryReadHeader(ReadOnlySpan<byte> data, out int width, out int height, out int pixelOffset) {
    width = 0;
    height = 0;
    pixelOffset = 0;

    var lineEnd = data.IndexOf((byte)'\n');
    if (lineEnd < 0 || lineEnd > _MaximumHeaderLength)
      return false;

    var line = data[..lineEnd];
    var offset = 0;
    if (!_TryReadPositiveInteger(line, ref offset, out width)
        || !_TryReadPositiveInteger(line, ref offset, out height))
      return false;

    _SkipWhitespace(line, ref offset);
    if (offset != line.Length)
      return false;

    if ((long)width * height > MtvFile.MaximumPixels)
      return false;

    pixelOffset = lineEnd + 1;
    return true;
  }

  private static bool _TryReadPositiveInteger(ReadOnlySpan<byte> text, ref int offset, out int value) {
    value = 0;
    _SkipWhitespace(text, ref offset);
    if (offset >= text.Length)
      return false;

    if (text[offset] == (byte)'+')
      ++offset;
    else if (text[offset] == (byte)'-')
      return false;

    var firstDigit = offset;
    while (offset < text.Length && text[offset] is >= (byte)'0' and <= (byte)'9') {
      var digit = text[offset++] - (byte)'0';
      if (value > (int.MaxValue - digit) / 10)
        return false;
      value = value * 10 + digit;
    }

    return offset != firstDigit && value > 0;
  }

  private static void _SkipWhitespace(ReadOnlySpan<byte> text, ref int offset) {
    while (offset < text.Length && _IsAsciiWhitespace(text[offset]))
      ++offset;
  }

  private static bool _IsAsciiWhitespace(byte value)
    => value == (byte)' ' || value is >= 0x09 and <= 0x0D;
}
