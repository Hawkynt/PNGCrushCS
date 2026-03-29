using System;

namespace FileFormat.Fli;

/// <summary>Decodes FLI/FLC delta and ByteRun compressed frame data.</summary>
internal static class FliDeltaDecoder {

  private const int _FRAME_HEADER_BYTE_COUNT = 1;

  /// <summary>
  ///   Decodes a ByteRun (chunk type 15) compressed frame into raw pixel data.
  ///   Each row starts with a packet count byte, followed by packets.
  ///   Packet: if count byte is positive, repeat next byte count times.
  ///   If count byte is negative (signed), |count| literal bytes follow.
  /// </summary>
  public static byte[] DecodeByteRun(byte[] data, int width, int height) {
    ArgumentNullException.ThrowIfNull(data);

    var pixels = new byte[width * height];
    var inIdx = 0;

    for (var row = 0; row < height; ++row) {
      var rowStart = row * width;
      var col = 0;

      if (inIdx >= data.Length)
        break;

      // First byte of each row is the packet count (informational, we use col < width instead)
      var packetCount = data[inIdx++];
      _ = packetCount;

      while (col < width && inIdx < data.Length) {
        var count = (sbyte)data[inIdx++];

        if (count > 0) {
          // Repeat: next byte repeated count times
          if (inIdx >= data.Length)
            break;

          var value = data[inIdx++];
          var run = Math.Min(count, width - col);
          for (var j = 0; j < run; ++j)
            pixels[rowStart + col++] = value;
        } else if (count < 0) {
          // Literal: |count| literal bytes follow
          var literalCount = Math.Min(-count, width - col);
          for (var j = 0; j < literalCount && inIdx < data.Length; ++j)
            pixels[rowStart + col++] = data[inIdx++];
        }
        // count == 0: no-op (shouldn't normally occur)
      }
    }

    return pixels;
  }
}
