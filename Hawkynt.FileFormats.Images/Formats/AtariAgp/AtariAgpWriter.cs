using System;

namespace FileFormat.AtariAgp;

/// <summary>Assembles Atari 8-bit AGP image bytes from an <see cref="AtariAgpFile"/>.</summary>
public static class AtariAgpWriter {

  public static byte[] ToBytes(AtariAgpFile file) {
    var result = new byte[AtariAgpFile.FileSize];
    result[0] = (byte)file.Mode;

    var registers = file.Registers ?? [];
    registers.AsSpan(0, Math.Min(AtariAgpFile.RegisterCount, registers.Length)).CopyTo(result.AsSpan(1));

    var bitmap = file.Bitmap ?? [];
    var length = Math.Min(AtariAgpFile.FileSize - AtariAgpFile.BitmapOffset, bitmap.Length);
    bitmap.AsSpan(0, length).CopyTo(result.AsSpan(AtariAgpFile.BitmapOffset));

    return result;
  }
}
