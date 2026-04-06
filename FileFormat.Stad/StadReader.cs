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

  public static StadFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static StadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4)
      throw new InvalidDataException($"STAD data too small: expected at least 4 bytes, got {data.Length}.");

    // Check for pM85 or pM86 magic
    if (_HasMagic(data, _MagicPM85) || _HasMagic(data, _MagicPM86))
      return new StadFile { RawData = _Decompress(data, 4) };

    // Fallback: treat as raw 32000-byte uncompressed screen data
    if (data.Length == StadFile.ScreenDataSize) {
      var rawData = new byte[StadFile.ScreenDataSize];
      data.AsSpan(0, StadFile.ScreenDataSize).CopyTo(rawData);
      return new StadFile { RawData = rawData };
    }

    throw new InvalidDataException("Invalid STAD data: unrecognized magic and size is not 32000 bytes.");
  }

  private static bool _HasMagic(byte[] data, byte[] magic) {
    for (var i = 0; i < magic.Length; ++i)
      if (data[i] != magic[i])
        return false;
    return true;
  }

  /// <summary>Decompresses PackBits-style RLE data starting at the given offset.</summary>
  private static byte[] _Decompress(byte[] data, int offset) {
    var output = new List<byte>(StadFile.ScreenDataSize);
    var pos = offset;

    while (pos < data.Length && output.Count < StadFile.ScreenDataSize) {
      var control = (sbyte)data[pos++];
      if (control < 0) {
        // Run: repeat next byte (-control + 1) times
        var count = -control + 1;
        if (pos >= data.Length)
          break;
        var value = data[pos++];
        for (var i = 0; i < count && output.Count < StadFile.ScreenDataSize; ++i)
          output.Add(value);
      } else {
        // Literal: copy (control + 1) bytes
        var count = control + 1;
        for (var i = 0; i < count && pos < data.Length && output.Count < StadFile.ScreenDataSize; ++i)
          output.Add(data[pos++]);
      }
    }

    // Pad to 32000 if needed
    while (output.Count < StadFile.ScreenDataSize)
      output.Add(0);

    var result = new byte[StadFile.ScreenDataSize];
    output.CopyTo(0, result, 0, StadFile.ScreenDataSize);
    return result;
  }
}
