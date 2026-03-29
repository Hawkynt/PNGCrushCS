using System;
using System.IO;

namespace FileFormat.Flif;

/// <summary>Encodes and decodes FLIF variable-length integers.</summary>
internal static class FlifVarint {

  /// <summary>Decodes a varint from the given span starting at <paramref name="offset"/>, advancing the offset past the consumed bytes.</summary>
  public static int Decode(ReadOnlySpan<byte> data, ref int offset) {
    var result = 0;
    var shift = 0;
    while (offset < data.Length) {
      var b = data[offset++];
      result |= (b & 0x7F) << shift;
      if ((b & 0x80) == 0)
        return result;
      shift += 7;
      if (shift > 28)
        throw new InvalidDataException("FLIF varint exceeds 32-bit range.");
    }

    throw new InvalidDataException("Unexpected end of data while reading FLIF varint.");
  }

  /// <summary>Encodes a non-negative integer as a FLIF varint and writes it into the span at <paramref name="offset"/>, advancing the offset.</summary>
  public static void Encode(Span<byte> buffer, ref int offset, int value) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value), "FLIF varint must be non-negative.");

    var remaining = (uint)value;
    do {
      var b = (byte)(remaining & 0x7F);
      remaining >>= 7;
      if (remaining != 0)
        b |= 0x80;
      buffer[offset++] = b;
    } while (remaining != 0);
  }

  /// <summary>Returns the number of bytes required to encode <paramref name="value"/> as a varint.</summary>
  public static int EncodedLength(int value) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value), "FLIF varint must be non-negative.");

    var remaining = (uint)value;
    var length = 0;
    do {
      ++length;
      remaining >>= 7;
    } while (remaining != 0);

    return length;
  }
}
