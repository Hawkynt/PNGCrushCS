using System;
using System.Buffers.Binary;

namespace FileFormat.LogoPainter;

/// <summary>Assembles Logo Painter 3 picture bytes.</summary>
public static class LogoPainterWriter {

  public static byte[] ToBytes(LogoPainterFile file) {
    ArgumentNullException.ThrowIfNull(file.Screen);
    ArgumentNullException.ThrowIfNull(file.CharacterSet);

    var result = new byte[LogoPainterFile.ExpectedFileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    // The tail of the screen page is what the display routine reads its colours from, so it starts
    // as the 0xFF that means none were saved and is only written over where they were.
    result.AsSpan(LogoPainterFile.ScreenOffset + LogoPainterFile.Columns * LogoPainterFile.Rows,
      LogoPainterFile.ScreenStride - LogoPainterFile.Columns * LogoPainterFile.Rows).Fill(0xFF);

    file.Screen.AsSpan(0, Math.Min(file.Screen.Length, LogoPainterFile.Columns * LogoPainterFile.Rows))
      .CopyTo(result.AsSpan(LogoPainterFile.ScreenOffset));
    file.CharacterSet.AsSpan(0, Math.Min(file.CharacterSet.Length, LogoPainterFile.CharacterSetSize))
      .CopyTo(result.AsSpan(LogoPainterFile.CharacterSetOffset));

    if (file.Colors is { Length: 4 }) {
      result[LogoPainterFile.BackgroundRegisterOffset] = file.Colors[0];
      result[LogoPainterFile.MulticolorRegister1Offset] = file.Colors[1];
      result[LogoPainterFile.MulticolorRegister2Offset] = file.Colors[2];
      result[LogoPainterFile.ColorMemoryOffset] = (byte)(file.Colors[3] | 0x08);
    }

    return result;
  }
}
