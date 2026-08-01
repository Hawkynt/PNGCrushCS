using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>The run-length coding Apple named PackBits, which a dozen formats borrowed.</summary>
/// <remarks>
/// A command byte governs what follows: a value below 128 introduces that many literals plus one,
/// 128 is skipped, and anything above repeats the next byte 257 minus the command times. Counting
/// one less than the number is what lets a single byte cover a run of 128.
/// </remarks>
public static class PackBits {

  /// <summary>The longest run or literal block a command can cover.</summary>
  public const int MaxRun = 128;

  /// <summary>Unpacks a stream, stopping when the target is full or the stream ends.</summary>
  public static byte[] Unpack(ReadOnlySpan<byte> packed, int length) {
    var result = new byte[length];
    var at = 0;
    var written = 0;

    while (written < length && at < packed.Length) {
      var command = packed[at++];
      if (command == MaxRun)
        continue;

      if (command < MaxRun) {
        var count = command + 1;
        for (var i = 0; i < count && written < length && at < packed.Length; ++i)
          result[written++] = packed[at++];

        continue;
      }

      if (at >= packed.Length)
        break;

      var value = packed[at++];
      for (var i = 0; i < 257 - command && written < length; ++i)
        result[written++] = value;
    }

    return result;
  }

  /// <summary>Packs a stream.</summary>
  public static byte[] Pack(ReadOnlySpan<byte> data) {
    var result = new List<byte>(data.Length);
    var i = 0;

    while (i < data.Length) {
      var run = 1;
      while (i + run < data.Length && run < MaxRun && data[i + run] == data[i])
        ++run;

      if (run >= 3) {
        result.Add((byte)(257 - run));
        result.Add(data[i]);
        i += run;
        continue;
      }

      // Literals up to the point a run worth coding begins; two of a kind is not worth breaking for.
      var start = i;
      while (i < data.Length && i - start < MaxRun) {
        var ahead = 1;
        while (i + ahead < data.Length && ahead < 3 && data[i + ahead] == data[i])
          ++ahead;

        if (ahead >= 3)
          break;

        ++i;
      }

      result.Add((byte)(i - start - 1));
      for (var j = start; j < i; ++j)
        result.Add(data[j]);
    }

    return result.ToArray();
  }
}
