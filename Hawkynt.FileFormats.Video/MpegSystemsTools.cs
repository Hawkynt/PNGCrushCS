using System;

namespace Hawkynt.FileFormats.Video;

/// <summary>Bit-level primitives shared by MPEG-2 systems containers.</summary>
internal static class MpegSystemsTools {

  /// <summary>Computes the H.222.0 Annex A CRC-32, MSB first with polynomial 0x04C11DB7.</summary>
  internal static uint Crc32(ReadOnlySpan<byte> data) {
    var crc = 0xFFFFFFFFu;
    foreach (var value in data) {
      crc ^= (uint)value << 24;
      for (var bit = 0; bit < 8; ++bit)
        crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
    }

    return crc;
  }
}
