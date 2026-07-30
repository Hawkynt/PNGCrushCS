using System;

namespace FileFormat.MsxScreen2;

/// <summary>Assembles MSX Screen 2 file bytes from a <see cref="MsxScreen2File"/>.</summary>
public static class MsxScreen2Writer {

  public static byte[] ToBytes(MsxScreen2File file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PatternGenerator, file.ColorTable, file.PatternNameTable, file.HasBsaveHeader);
  }

  internal static byte[] _Assemble(byte[] patternGenerator, byte[] colorTable, byte[] patternNameTable, bool writeBsaveHeader) {
    var headerLength = writeBsaveHeader ? MsxScreen2File.BsaveHeaderSize : 0;
    var result = new byte[headerLength + MsxScreen2File.VramDataSize];

    if (writeBsaveHeader) {
      const ushort startAddress = 0x0000;
      var endAddress = (ushort)(startAddress + MsxScreen2File.VramDataSize - 1);
      result[0] = MsxScreen2File.BsaveMagic;
      result[1] = (byte)(startAddress & 0xFF);
      result[2] = (byte)(startAddress >> 8);
      result[3] = (byte)(endAddress & 0xFF);
      result[4] = (byte)(endAddress >> 8);
      result[5] = 0x00;
      result[6] = 0x00;
    }

    // The three tables sit where video memory puts them, not one after another.
    var vram = result.AsSpan(headerLength);
    patternGenerator.AsSpan(0, Math.Min(patternGenerator.Length, MsxScreen2File.PatternGeneratorSize))
      .CopyTo(vram[MsxScreen2File.PatternGeneratorOffset..]);
    patternNameTable.AsSpan(0, Math.Min(patternNameTable.Length, MsxScreen2File.PatternNameTableSize))
      .CopyTo(vram[MsxScreen2File.PatternNameTableOffset..]);
    colorTable.AsSpan(0, Math.Min(colorTable.Length, MsxScreen2File.ColorTableSize))
      .CopyTo(vram[MsxScreen2File.ColorTableOffset..]);

    return result;
  }
}
