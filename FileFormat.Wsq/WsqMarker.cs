using System;

namespace FileFormat.Wsq;

/// <summary>WSQ marker codes (JPEG-like two-byte markers starting with 0xFF).</summary>
internal static class WsqMarker {

  /// <summary>Start of Image.</summary>
  public const ushort SOI = 0xFFA0;

  /// <summary>End of Image.</summary>
  public const ushort EOI = 0xFFA1;

  /// <summary>Start of Frame.</summary>
  public const ushort SOF = 0xFFA2;

  /// <summary>Start of Block (subband header).</summary>
  public const ushort SOB = 0xFFA3;

  /// <summary>Define Transform Table.</summary>
  public const ushort DTT = 0xFFA4;

  /// <summary>Define Quantization Table.</summary>
  public const ushort DQT = 0xFFA5;

  /// <summary>Define Huffman Table.</summary>
  public const ushort DHT = 0xFFA6;

  /// <summary>Start of Scan.</summary>
  public const ushort SOS = 0xFFAC;

  /// <summary>Comment.</summary>
  public const ushort COM = 0xFFFE;

  /// <summary>Reads a big-endian ushort from a span.</summary>
  public static ushort ReadUInt16BE(ReadOnlySpan<byte> data, int offset)
    => (ushort)((data[offset] << 8) | data[offset + 1]);

  /// <summary>Writes a big-endian ushort to a byte array.</summary>
  public static void WriteUInt16BE(byte[] data, int offset, ushort value) {
    data[offset] = (byte)(value >> 8);
    data[offset + 1] = (byte)(value & 0xFF);
  }
}
