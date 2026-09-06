using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.FunPainter;

/// <summary>The run-length encoding a packed Fun Painter payload uses.</summary>
/// <remarks>
/// One escape value, chosen per file and stored beside the flag that says the payload is packed.
/// A byte other than the escape stands for itself; the escape introduces a count and then the value
/// to repeat. A count of zero therefore emits nothing while still consuming its value byte, which
/// the packer never writes but the unpacker must tolerate, because it is what the format's own
/// decoder does.
/// <para/>
/// The escape can itself be the value being repeated, so a literal equal to the escape costs three
/// bytes rather than one. That is what makes the choice of escape worth making rather than fixing:
/// the rarest byte in the payload is the cheapest one to spend.
/// </remarks>
internal static class FunPainterRle {

  /// <summary>Expands a packed payload into exactly <paramref name="length"/> bytes.</summary>
  public static byte[] Unpack(ReadOnlySpan<byte> packed, byte escape, int length) {
    var unpacked = new byte[length];
    var read = 0;
    var written = 0;

    while (written < length) {
      if (read >= packed.Length)
        throw new InvalidDataException(
          $"The packed Fun Painter payload ends after {written} of {length} bytes.");

      int count;
      byte value;
      var first = packed[read++];
      if (first == escape) {
        if (read + 1 >= packed.Length)
          throw new InvalidDataException("A Fun Painter run is cut short by the end of the file.");

        count = packed[read++];
        value = packed[read++];
      } else {
        count = 1;
        value = first;
      }

      if (written + count > length)
        throw new InvalidDataException(
          $"A Fun Painter run overruns the picture by {written + count - length} bytes.");

      unpacked.AsSpan(written, count).Fill(value);
      written += count;
    }

    return unpacked;
  }

  /// <summary>The byte that costs least as an escape: whichever occurs least often.</summary>
  public static byte ChooseEscape(ReadOnlySpan<byte> body) {
    Span<int> counts = stackalloc int[256];
    foreach (var b in body)
      ++counts[b];

    var best = 0;
    for (var i = 1; i < counts.Length; ++i)
      if (counts[i] < counts[best])
        best = i;

    return (byte)best;
  }

  /// <summary>Packs a payload with the given escape.</summary>
  public static byte[] Pack(ReadOnlySpan<byte> body, byte escape) {
    var packed = new List<byte>(body.Length / 2 + 16);

    for (var at = 0; at < body.Length;) {
      var value = body[at];
      var run = 1;
      // A count is one byte, so a run longer than 255 is written as several.
      while (at + run < body.Length && body[at + run] == value && run < 255)
        ++run;

      // Three bytes buy the run; below three they cost more than the literals, except for the
      // escape itself, which cannot be written as a literal at all.
      if (run >= 3 || value == escape) {
        packed.Add(escape);
        packed.Add((byte)run);
        packed.Add(value);
      } else
        for (var i = 0; i < run; ++i)
          packed.Add(value);

      at += run;
    }

    return [.. packed];
  }
}
