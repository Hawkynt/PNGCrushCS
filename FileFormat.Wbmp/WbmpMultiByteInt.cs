using System;

namespace FileFormat.Wbmp;

/// <summary>Encodes and decodes WBMP multi-byte integer values (7 data bits per byte, MSB continuation bit).</summary>
internal static class WbmpMultiByteInt {

  /// <summary>Encodes a non-negative integer as a WBMP multi-byte integer.</summary>
  public static byte[] Encode(int value) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative.");

    if (value == 0)
      return [0];

    // Count how many 7-bit groups we need
    var temp = value;
    var byteCount = 0;
    while (temp > 0) {
      ++byteCount;
      temp >>= 7;
    }

    var result = new byte[byteCount];
    for (var i = byteCount - 1; i >= 0; --i) {
      result[i] = (byte)(value & 0x7F);
      value >>= 7;
    }

    // Set continuation bit on all bytes except the last
    for (var i = 0; i < byteCount - 1; ++i)
      result[i] |= 0x80;

    return result;
  }

  /// <summary>Decodes a WBMP multi-byte integer from a byte span.</summary>
  public static int Decode(ReadOnlySpan<byte> data, out int bytesConsumed) {
    var result = 0;
    bytesConsumed = 0;

    for (var i = 0; i < data.Length; ++i) {
      var b = data[i];
      result = (result << 7) | (b & 0x7F);
      ++bytesConsumed;

      if ((b & 0x80) == 0)
        return result;
    }

    throw new InvalidOperationException("Unterminated multi-byte integer.");
  }
}
