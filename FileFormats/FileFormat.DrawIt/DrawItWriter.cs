using System;

namespace FileFormat.DrawIt;

/// <summary>Assembles DrawIt (.dit) file bytes from a <see cref="DrawItFile"/>.</summary>
public static class DrawItWriter {

  public static byte[] ToBytes(DrawItFile file) {
    var result = new byte[DrawItFile.FileSize];

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, DrawItFile.BitmapDataSize)).CopyTo(result);

    var registers = file.ColorRegisters ?? [];
    registers.AsSpan(0, Math.Min(registers.Length, FileFormat.Core.Atari8BitGraphics.ColorRegisterCount))
      .CopyTo(result.AsSpan(DrawItFile.ColorRegisterOffset));

    return result;
  }
}
