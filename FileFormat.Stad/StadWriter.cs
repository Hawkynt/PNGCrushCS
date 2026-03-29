using System;
using System.Collections.Generic;

namespace FileFormat.Stad;

/// <summary>Assembles STAD compressed screen bytes from a <see cref="StadFile"/>.</summary>
public static class StadWriter {

  private static readonly byte[] _MagicPM86 = [(byte)'p', (byte)'M', (byte)'8', (byte)'6'];

  public static byte[] ToBytes(StadFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var screenData = new byte[StadFile.ScreenDataSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, StadFile.ScreenDataSize)).CopyTo(screenData);

    var compressed = _Compress(screenData);
    var result = new byte[_MagicPM86.Length + compressed.Length];
    _MagicPM86.AsSpan(0, _MagicPM86.Length).CopyTo(result);
    compressed.AsSpan(0, compressed.Length).CopyTo(result.AsSpan(_MagicPM86.Length));
    return result;
  }

  /// <summary>PackBits-style RLE compression.</summary>
  private static byte[] _Compress(byte[] data) {
    var output = new List<byte>(data.Length);
    var pos = 0;

    while (pos < data.Length) {
      // Check for a run
      var runStart = pos;
      while (pos + 1 < data.Length && data[pos] == data[pos + 1] && pos - runStart < 127)
        ++pos;

      if (pos > runStart) {
        // We have a run of (pos - runStart + 1) identical bytes
        var runLength = pos - runStart + 1;
        output.Add((byte)(-(runLength - 1) & 0xFF));
        output.Add(data[runStart]);
        ++pos;
      } else {
        // Literal sequence
        var literalStart = pos;
        while (pos < data.Length && (pos + 1 >= data.Length || data[pos] != data[pos + 1]) && pos - literalStart < 127)
          ++pos;

        var literalLength = pos - literalStart;
        output.Add((byte)(literalLength - 1));
        for (var i = 0; i < literalLength; ++i)
          output.Add(data[literalStart + i]);
      }
    }

    return output.ToArray();
  }
}
