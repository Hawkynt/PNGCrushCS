using System;

namespace FileFormat.MsxScreen8;

/// <summary>Assembles MSX2 Screen 8 file bytes from a <see cref="MsxScreen8File"/>.</summary>
public static class MsxScreen8Writer {

  public static byte[] ToBytes(MsxScreen8File file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.HasBsaveHeader);
  }

  internal static byte[] _Assemble(byte[] pixelData, bool writeBsaveHeader) {
    var headerLength = writeBsaveHeader ? MsxScreen8File.BsaveHeaderSize : 0;
    var result = new byte[headerLength + MsxScreen8File.PixelDataSize];

    if (writeBsaveHeader) {
      const ushort startAddress = 0x0000;
      var endAddress = (ushort)(startAddress + MsxScreen8File.PixelDataSize - 1);
      result[0] = MsxScreen8File.BsaveMagic;
      result[1] = (byte)(startAddress & 0xFF);
      result[2] = (byte)(startAddress >> 8);
      result[3] = (byte)(endAddress & 0xFF);
      result[4] = (byte)(endAddress >> 8);
      result[5] = 0x00;
      result[6] = 0x00;
    }

    pixelData.AsSpan(0, Math.Min(pixelData.Length, MsxScreen8File.PixelDataSize)).CopyTo(result.AsSpan(headerLength));

    return result;
  }
}
