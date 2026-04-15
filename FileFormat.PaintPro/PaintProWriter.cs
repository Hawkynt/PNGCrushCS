using System;

namespace FileFormat.PaintPro;

/// <summary>Assembles Paint Pro file bytes from an in-memory representation.</summary>
public static class PaintProWriter {

  public static byte[] ToBytes(PaintProFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PaintProFile.FileSize];
    var span = result.AsSpan();

    var header = new PaintProHeader((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(PaintProHeader.StructSize));

    return result;
  }
}
