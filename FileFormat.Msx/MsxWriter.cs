using System;

namespace FileFormat.Msx;

/// <summary>Assembles MSX2 screen dump file bytes from an <see cref="MsxFile"/>.</summary>
public static class MsxWriter {

  /// <summary>BLOAD header magic byte.</summary>
  private const byte _BLOAD_MAGIC = 0xFE;

  /// <summary>BLOAD header size in bytes.</summary>
  private const int _BLOAD_HEADER_SIZE = 7;

  public static byte[] ToBytes(MsxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Palette, file.HasBloadHeader, file.Mode);
  }

  internal static byte[] _Assemble(byte[] pixelData, byte[]? palette, bool writeBloadHeader, MsxMode mode) {
    var paletteLength = palette?.Length ?? 0;
    var dataLength = pixelData.Length + paletteLength;
    var headerLength = writeBloadHeader ? _BLOAD_HEADER_SIZE : 0;
    var result = new byte[headerLength + dataLength];

    if (writeBloadHeader) {
      var startAddress = _GetStartAddress(mode);
      var endAddress = (ushort)(startAddress + dataLength - 1);

      result[0] = _BLOAD_MAGIC;
      result[1] = (byte)(startAddress & 0xFF);
      result[2] = (byte)(startAddress >> 8);
      result[3] = (byte)(endAddress & 0xFF);
      result[4] = (byte)(endAddress >> 8);
      result[5] = 0x00; // exec address low
      result[6] = 0x00; // exec address high
    }

    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(headerLength));
    if (palette != null)
      palette.AsSpan(0, palette.Length).CopyTo(result.AsSpan(headerLength + pixelData.Length));

    return result;
  }

  private static ushort _GetStartAddress(MsxMode mode) => mode switch {
    MsxMode.Screen2 => 0x0000,
    MsxMode.Screen5 => 0x0000,
    MsxMode.Screen7 => 0x0000,
    MsxMode.Screen8 => 0x0000,
    _ => 0x0000
  };
}
