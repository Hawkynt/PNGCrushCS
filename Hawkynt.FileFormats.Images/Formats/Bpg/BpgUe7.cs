using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Bpg;

/// <summary>Reads and writes BPG's canonical 7-bit continuation integers.</summary>
internal static class BpgUe7 {

  /// <summary>Reads a <c>ue7(32)</c> value which must also fit this package's signed-size model.</summary>
  public static int Read(ReadOnlySpan<byte> data, ref int offset) {
    var value = ReadUInt32(data, ref offset);
    if (value > int.MaxValue)
      throw new InvalidDataException(
        $"A BPG ue7(32) value decoded to {value}, which exceeds the largest size this implementation can represent.");

    return (int)value;
  }

  /// <summary>Reads one canonical <c>ue7(32)</c> value.</summary>
  public static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset) {
    ulong value = 0;
    var first = true;

    for (var group = 0; group < 5; ++group) {
      if ((uint)offset >= (uint)data.Length)
        throw new InvalidDataException("The BPG file ended in the middle of a ue7(32) value.");

      var octet = data[offset++];
      if (first && octet == 0x80)
        throw new InvalidDataException(
          "A BPG ue7(32) value uses a leading zero continuation group. The format requires the shortest encoding.");

      first = false;
      value = (value << 7) | (uint)(octet & 0x7f);
      if (value > uint.MaxValue)
        throw new InvalidDataException("A BPG ue7(32) value exceeds the 32 bits the format allows.");

      if ((octet & 0x80) == 0)
        return (uint)value;
    }

    throw new InvalidDataException("A BPG ue7(32) value continues past the five bytes a 32-bit value can occupy.");
  }

  /// <summary>Writes the shortest representation of a non-negative value.</summary>
  public static void Write(List<byte> output, int value) {
    ArgumentNullException.ThrowIfNull(output);
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value));

    Span<byte> groups = stackalloc byte[5];
    var count = 0;
    do {
      groups[count++] = (byte)(value & 0x7f);
      value >>= 7;
    } while (value != 0);

    for (var i = count - 1; i >= 0; --i)
      output.Add((byte)(groups[i] | (i == 0 ? 0 : 0x80)));
  }
}
