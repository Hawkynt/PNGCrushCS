using System;
using System.Collections.Generic;

namespace FileFormat.Bpg;

/// <summary>Unsigned exp-Golomb 7-bit variable-length integer encoding used by BPG.</summary>
internal static class BpgUe7 {

  /// <summary>Reads a ue7-encoded integer from the given data at the specified offset, advancing the offset past the encoded bytes.</summary>
  public static int Read(ReadOnlySpan<byte> data, ref int offset) {
    var value = 0;
    byte b;
    do {
      if (offset >= data.Length)
        throw new InvalidOperationException("Unexpected end of data while reading ue7 value.");

      b = data[offset++];
      value = (value << 7) | (b & 0x7F);
    } while ((b & 0x80) != 0);

    return value;
  }

  /// <summary>Writes a ue7-encoded integer to the output list.</summary>
  public static void Write(List<byte> output, int value) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be non-negative.");

    // Determine how many 7-bit groups we need
    if (value == 0) {
      output.Add(0);
      return;
    }

    // Count how many 7-bit groups
    var temp = value;
    var groups = 0;
    while (temp > 0) {
      ++groups;
      temp >>= 7;
    }

    // Write groups from most significant to least significant
    for (var i = groups - 1; i >= 0; --i) {
      var chunk = (byte)((value >> (i * 7)) & 0x7F);
      if (i > 0)
        chunk |= 0x80;

      output.Add(chunk);
    }
  }
}
