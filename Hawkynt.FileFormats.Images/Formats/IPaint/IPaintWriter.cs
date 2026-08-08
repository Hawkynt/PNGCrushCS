using System;
using System.Collections.Generic;

namespace FileFormat.IPaint;

/// <summary>Assembles I Paint bytes from an <see cref="IPaintFile"/>.</summary>
public static class IPaintWriter {

  /// <summary>Where the packed bitmap starts.</summary>
  private const int _BITMAP_OFFSET = 18;

  /// <summary>The longest run or run of literals a command byte can count.</summary>
  private const int _MAX_RUN = 127;

  public static byte[] ToBytes(IPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var data = new List<byte>(_BITMAP_OFFSET + (file.Bitmap?.Length ?? 0));
    data.AddRange(new byte[_BITMAP_OFFSET]);

    data[2] = (byte)'B';
    data[3] = (byte)'R';
    data[4] = (byte)'U';
    data[5] = (byte)'S';
    data[6] = 4;
    data[10] = 1;
    data[11] = 2;
    data[12] = (byte)file.Columns;
    data[13] = (byte)file.Height;
    data[14] = (byte)(file.Height >> 8);

    _Pack(data, file.Bitmap ?? []);

    var colors = file.Colors ?? [];
    if (colors.Length == 0)
      return [.. data];

    // The tag moves the read position without ending whatever run is in progress, so a run is never
    // allowed to straddle it here — the reader would carry one across, but nothing else would.
    data.Add((byte)'C');
    data.Add((byte)'O');
    data.Add((byte)'L');
    data.Add((byte)'R');
    _Pack(data, colors);

    return [.. data];
  }

  /// <summary>
  /// Codes one section as runs, whose command byte's top bit says whether a single value follows or
  /// that many bytes to take as they are.
  /// </summary>
  /// <remarks>
  /// A run costs two bytes and a literal one, so two equal bytes are only worth naming when they do
  /// not interrupt a run of literals that would then have to be closed and reopened — three is the
  /// shortest run that pays for itself.
  /// </remarks>
  private static void _Pack(List<byte> data, ReadOnlySpan<byte> section) {
    var literals = 0;

    for (var at = 0; at < section.Length;) {
      var run = 1;
      while (run < _MAX_RUN && at + run < section.Length && section[at + run] == section[at])
        ++run;

      if (run >= 3) {
        _Flush(data, section, at, ref literals);
        data.Add((byte)(128 | run));
        data.Add(section[at]);
        at += run;
        continue;
      }

      ++literals;
      ++at;

      if (literals == _MAX_RUN)
        _Flush(data, section, at, ref literals);
    }

    _Flush(data, section, section.Length, ref literals);
  }

  private static void _Flush(List<byte> data, ReadOnlySpan<byte> section, int end, ref int literals) {
    if (literals == 0)
      return;

    data.Add((byte)literals);
    for (var i = end - literals; i < end; ++i)
      data.Add(section[i]);

    literals = 0;
  }
}
