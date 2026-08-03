using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Stad;

/// <summary>Reads STAD compressed Atari ST screen images from bytes, streams, or file paths.</summary>
public static class StadReader {

  private static readonly byte[] _MagicPM85 = [(byte)'p', (byte)'M', (byte)'8', (byte)'5'];
  private static readonly byte[] _MagicPM86 = [(byte)'p', (byte)'M', (byte)'8', (byte)'6'];

  public static StadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("STAD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static StadFile FromStream(Stream stream) {
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

  public static StadFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException($"STAD data too small: expected at least 4 bytes, got {data.Length}.");

    // Check for pM85 or pM86 magic
    if (_HasMagic(data, _MagicPM85) || _HasMagic(data, _MagicPM86)) {
      if (data.Length < _HEADER_SIZE)
        throw new InvalidDataException($"A STAD header is {_HEADER_SIZE} bytes; this file is {data.Length}.");

      var screen = _Decompress(data);

      // pM86 stores the screen a byte-column at a time rather than a row at a time.
      return new StadFile { RawData = _HasMagic(data, _MagicPM86) ? _Transpose(screen) : screen };
    }

    // Fallback: treat as raw 32000-byte uncompressed screen data
    if (data.Length == StadFile.ScreenDataSize) {
      var rawData = new byte[StadFile.ScreenDataSize];
      data.Slice(0, StadFile.ScreenDataSize).CopyTo(rawData);
      return new StadFile { RawData = rawData };
    }

    throw new InvalidDataException("Invalid STAD data: unrecognized magic and size is not 32000 bytes.");
  }

  public static StadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static bool _HasMagic(ReadOnlySpan<byte> data, byte[] magic) {
    for (var i = 0; i < magic.Length; ++i)
      if (data[i] != magic[i])
        return false;
    return true;
  }

  /// <summary>Bytes ahead of the packed screen: the magic, two bytes for one escape and three for the other.</summary>
  private const int _HEADER_SIZE = 7;

  /// <summary>
  /// Expands the packed screen.
  /// </summary>
  /// <remarks>
  /// This was read as PackBits, which STAD is not, and the header was taken to be four bytes when it
  /// is seven — so all three samples came back as noise where RECOIL and XnView agree on the picture.
  /// <para/>
  /// The three bytes after the magic set up two escapes, both chosen per file from bytes the screen
  /// makes little use of:
  /// <list type="bullet">
  ///   <item>Byte 4 escapes a run of one particular value, and byte 5 is that value — whichever of
  ///     0x00 or 0xFF the picture is mostly made of. One count byte follows, and it counts from
  ///     nought, so a run is one longer than it says.</item>
  ///   <item>Byte 6 escapes a run of anything else: the value follows, then the count, again one
  ///     less than the run.</item>
  ///   <item>Any other byte stands for itself.</item>
  /// </list>
  /// Worked out by rebuilding the screens RECOIL draws and reading the files against them. All three
  /// samples now expand to exactly the 32000 bytes a high-resolution screen takes and match RECOIL
  /// byte for byte.
  /// </remarks>
  private static byte[] _Decompress(ReadOnlySpan<byte> data) {
    var escapeRun = data[4];
    var runValue = data[5];
    var escapeAny = data[6];

    var screen = new byte[StadFile.ScreenDataSize];
    var written = 0;
    var pos = _HEADER_SIZE;

    while (pos < data.Length && written < StadFile.ScreenDataSize) {
      var control = data[pos++];

      if (control == escapeRun) {
        if (pos >= data.Length)
          break;

        var run = Math.Min(data[pos++] + 1, StadFile.ScreenDataSize - written);
        screen.AsSpan(written, run).Fill(runValue);
        written += run;
        continue;
      }

      if (control == escapeAny) {
        if (pos + 1 >= data.Length)
          break;

        var value = data[pos++];
        var run = Math.Min(data[pos++] + 1, StadFile.ScreenDataSize - written);
        screen.AsSpan(written, run).Fill(value);
        written += run;
        continue;
      }

      screen[written++] = control;
    }

    return screen;
  }

  /// <summary>Puts a screen stored a byte-column at a time back into rows.</summary>
  private static byte[] _Transpose(byte[] columns) {
    var rows = new byte[columns.Length];

    for (var column = 0; column < StadFile.BytesPerRow; ++column)
      for (var row = 0; row < StadFile.PixelHeight; ++row)
        rows[row * StadFile.BytesPerRow + column] = columns[column * StadFile.PixelHeight + row];

    return rows;
  }
}
