using System;

namespace FileFormat.CokeAtari;

/// <summary>The 18-byte header of a COKE file.</summary>
/// <remarks>
/// Layout: the ASCII signature <c>COKE format.</c>, then width and height as big-endian 16-bit
/// values, then a fixed two-byte trailer of <c>00 12</c>. RGB565 big-endian pixel data starts
/// immediately afterwards, so a file measures exactly <c>18 + width * height * 2</c> bytes.
/// </remarks>
public static class CokeAtariHeader {

  /// <summary>ASCII signature every COKE file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => "COKE format."u8;

  /// <summary>Total header size in bytes.</summary>
  public const int StructSize = 18;

  /// <summary>Offset of the big-endian width/height pair.</summary>
  public const int DimensionsOffset = 12;

  private const byte _TRAILER_HIGH = 0x00;
  private const byte _TRAILER_LOW = 0x12;

  /// <summary>Reads width and height; returns <c>false</c> when this is not a COKE header.</summary>
  public static bool TryRead(ReadOnlySpan<byte> data, out int width, out int height) {
    width = height = 0;
    if (data.Length < StructSize || !data[..Signature.Length].SequenceEqual(Signature))
      return false;

    if (data[16] != _TRAILER_HIGH || data[17] != _TRAILER_LOW)
      return false;

    width = (data[DimensionsOffset] << 8) | data[DimensionsOffset + 1];
    height = (data[DimensionsOffset + 2] << 8) | data[DimensionsOffset + 3];
    return true;
  }

  /// <summary>Writes the header into <paramref name="destination"/>.</summary>
  public static void Write(Span<byte> destination, int width, int height) {
    Signature.CopyTo(destination);
    destination[DimensionsOffset] = (byte)(width >> 8);
    destination[DimensionsOffset + 1] = (byte)width;
    destination[DimensionsOffset + 2] = (byte)(height >> 8);
    destination[DimensionsOffset + 3] = (byte)height;
    destination[16] = _TRAILER_HIGH;
    destination[17] = _TRAILER_LOW;
  }
}
