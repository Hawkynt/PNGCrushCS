using System;
using System.IO;

namespace FileFormat.Fli;

/// <summary>Encodes raw pixel data into FLI/FLC ByteRun compressed format.</summary>
internal static class FliDeltaEncoder {

  /// <summary>
  ///   Encodes raw pixel data into ByteRun (chunk type 15) format.
  ///   Each row starts with a packet count byte, followed by packets.
  ///   Packet: if count byte is positive, repeat next byte count times.
  ///   If count byte is negative (signed), |count| literal bytes follow.
  /// </summary>
  public static byte[] EncodeByteRun(byte[] pixels, int width, int height) {
    ArgumentNullException.ThrowIfNull(pixels);

    using var ms = new MemoryStream();

    for (var row = 0; row < height; ++row) {
      var rowStart = row * width;

      // Reserve a byte for the packet count; we'll patch it after encoding the row
      var packetCountPos = ms.Position;
      ms.WriteByte(0);
      var packetCount = 0;
      var col = 0;

      while (col < width) {
        // Check for a run of identical bytes
        if (col + 1 < width && pixels[rowStart + col] == pixels[rowStart + col + 1]) {
          var value = pixels[rowStart + col];
          var runStart = col;
          while (col < width && col - runStart < 127 && pixels[rowStart + col] == value)
            ++col;

          var run = col - runStart;
          ms.WriteByte((byte)run); // positive = repeat
          ms.WriteByte(value);
          ++packetCount;
        } else {
          // Literal run
          var literalStart = col;
          while (col < width && col - literalStart < 127) {
            if (col + 1 < width && pixels[rowStart + col] == pixels[rowStart + col + 1])
              break;
            ++col;
          }

          var literalCount = col - literalStart;
          ms.WriteByte(unchecked((byte)(-(sbyte)literalCount))); // negative = literal
          ms.Write(pixels, rowStart + literalStart, literalCount);
          ++packetCount;
        }
      }

      // Patch packet count
      var currentPos = ms.Position;
      ms.Position = packetCountPos;
      ms.WriteByte((byte)packetCount);
      ms.Position = currentPos;
    }

    return ms.ToArray();
  }
}
