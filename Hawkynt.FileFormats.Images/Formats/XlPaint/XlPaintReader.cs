using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.XlPaint;

/// <summary>Reads XL-Paint pictures from bytes, streams, or file paths.</summary>
public static class XlPaintReader {

  public static XlPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XlPaintFile FromStream(Stream stream) {
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

  public static XlPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length >= 10
        && Encoding.ASCII.GetString(data[..XlPaintFile.Signature.Length]) == XlPaintFile.Signature) {
      // A marked file says where its picture starts and how tall it is; anything the stream does
      // not reach stays black rather than being an error.
      // Sized to the picture rather than to the largest one there is, so the length of the array
      // says how tall the picture is instead of merely being big enough.
      var marked = new byte[192 * 2 * XlPaintFile.Stride];
      _TryUnpack(data, 8, marked, marked.Length);

      return new() { ScreenData = marked, Height = 192, Registers = Atari8BitGraphics.ReadPf012Bak(data, 4) };
    }

    if (data.Length < 8)
      throw new InvalidDataException($"Not an XL-Paint picture: {data.Length} bytes.");

    // Unmarked files say nothing about their height, so the only test is which length the stream
    // fills exactly — 200 rows first, because a 192-row picture cannot fill the longer one.
    foreach (var height in (int[])[200, 192]) {
      var screens = new byte[height * 2 * XlPaintFile.Stride];
      if (!_TryUnpack(data, 4, screens, screens.Length))
        continue;

      return new() { ScreenData = screens, Height = height, Registers = Atari8BitGraphics.ReadPf012Bak(data, 0) };
    }

    throw new InvalidDataException("An XL-Paint picture's stream fills neither 192 nor 200 rows.");
  }

  /// <summary>
  /// Unpacks the run-length encoding into columns, returning whether the stream sufficed.
  /// </summary>
  /// <remarks>
  /// A command byte is a count with its top bit choosing a repeated value over a run of literals,
  /// and a count of 64 or more is not a count at all: its low bits are the high byte of a longer
  /// one. So the encoding spends six bits on the common case and fourteen on the rest, which is
  /// what a column of one interlaced screen needs — those runs are hundreds of bytes long.
  /// </remarks>
  private static bool _TryUnpack(ReadOnlySpan<byte> data, int offset, Span<byte> target, int end) {
    var at = offset;
    var remaining = 0;
    var value = -1;

    for (var column = 0; column < XlPaintFile.Stride; ++column)
    for (var position = column; position < end; position += XlPaintFile.Stride) {
      while (remaining == 0) {
        if (at >= data.Length)
          return false;

        var command = data[at++];
        var repeated = command >= 128;
        var count = repeated ? command - 128 : command;

        if (count >= 64) {
          if (at >= data.Length)
            return false;

          count = ((count - 64) << 8) | data[at++];
        }

        remaining = count;
        if (!repeated) {
          value = -1;
          continue;
        }

        if (at >= data.Length)
          return false;

        value = data[at++];
      }

      --remaining;
      if (value >= 0)
        target[position] = (byte)value;
      else {
        if (at >= data.Length)
          return false;

        target[position] = data[at++];
      }
    }

    return true;
  }

  public static XlPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
