using System;

namespace FileFormat.Paradox;

/// <summary>Assembles Atari 8-bit Paradox (.mcpp) screen bytes.</summary>
public static class ParadoxWriter {

  public static byte[] ToBytes(ParadoxFile file) {
    var result = new byte[ParadoxFile.FileSize];

    _Copy(file.FirstField, result, 0, ParadoxFile.FieldDataSize);
    _Copy(file.SecondField, result, ParadoxFile.SecondFieldOffset, ParadoxFile.FieldDataSize);
    _Copy(file.FirstFieldColors, result, ParadoxFile.ColorsOffset, ParadoxFile.ColorsPerField);
    _Copy(file.SecondFieldColors, result, ParadoxFile.ColorsOffset + ParadoxFile.ColorsPerField, ParadoxFile.ColorsPerField);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
