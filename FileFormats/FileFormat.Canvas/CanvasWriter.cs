using System;

namespace FileFormat.Canvas;

/// <summary>Assembles Canvas ST file bytes from an in-memory representation.</summary>
public static class CanvasWriter {

  public static byte[] ToBytes(CanvasFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CanvasFile.FileSize];
    var span = result.AsSpan();

    var header = new CanvasHeader((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(CanvasHeader.StructSize));

    return result;
  }
}
