using System;
using FileFormat.Core;

namespace FileFormat.MagicPainter;

/// <summary>Assembles Magic Painter (.mgp) file bytes from a <see cref="MagicPainterFile"/>.</summary>
public static class MagicPainterWriter {

  public static byte[] ToBytes(MagicPainterFile file) {
    var result = new byte[MagicPainterFile.FileSize];

    var registers = file.ColorRegisters ?? [];
    registers.AsSpan(0, Math.Min(registers.Length, Atari8BitGraphics.ColorRegisterCount)).CopyTo(result);
    result[MagicPainterFile.RainbowOffset] = file.Rainbow;

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, MagicPainterFile.StoredBitmapSize))
      .CopyTo(result.AsSpan(MagicPainterFile.BitmapOffset));

    return result;
  }
}
