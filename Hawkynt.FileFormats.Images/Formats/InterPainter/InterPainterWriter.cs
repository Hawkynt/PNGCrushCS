using System;

namespace FileFormat.InterPainter;

/// <summary>Assembles InterPainter file bytes from an <see cref="InterPainterFile"/>.</summary>
public static class InterPainterWriter {

  public static byte[] ToBytes(InterPainterFile file) {
    var result = new byte[InterPainterFile.FileSize];

    _Copy(file.FirstFrame, result, 0, InterPainterFile.FrameDataSize);
    _Copy(file.SecondFrame, result, InterPainterFile.SecondFrameOffset, InterPainterFile.FrameDataSize);
    _Copy(file.Colors, result, InterPainterFile.ColorsOffset, InterPainterFile.ColorCount);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
