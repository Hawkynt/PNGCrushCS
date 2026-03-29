using System;

namespace FileFormat.MsxScreen5;

/// <summary>Assembles MSX2 Screen 5 file bytes from a <see cref="MsxScreen5File"/>.</summary>
public static class MsxScreen5Writer {

  public static byte[] ToBytes(MsxScreen5File file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Palette, file.HasBsaveHeader);
  }

  internal static byte[] _Assemble(byte[] pixelData, byte[]? palette, bool writeBsaveHeader) {
    var paletteLength = palette?.Length ?? 0;
    var dataLength = MsxScreen5File.PixelDataSize + paletteLength;
    var headerLength = writeBsaveHeader ? MsxScreen5File.BsaveHeaderSize : 0;
    var result = new byte[headerLength + dataLength];

    if (writeBsaveHeader) {
      const ushort startAddress = 0x0000;
      var endAddress = (ushort)(startAddress + dataLength - 1);
      result[0] = MsxScreen5File.BsaveMagic;
      result[1] = (byte)(startAddress & 0xFF);
      result[2] = (byte)(startAddress >> 8);
      result[3] = (byte)(endAddress & 0xFF);
      result[4] = (byte)(endAddress >> 8);
      result[5] = 0x00;
      result[6] = 0x00;
    }

    pixelData.AsSpan(0, Math.Min(pixelData.Length, MsxScreen5File.PixelDataSize)).CopyTo(result.AsSpan(headerLength));
    if (palette != null)
      palette.AsSpan(0, Math.Min(palette.Length, MsxScreen5File.PaletteSize)).CopyTo(result.AsSpan(headerLength + MsxScreen5File.PixelDataSize));

    return result;
  }
}
