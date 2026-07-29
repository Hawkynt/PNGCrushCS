using System;

namespace FileFormat.AladdinPaint;

/// <summary>Assembles Aladdin Paint file bytes from an in-memory representation.</summary>
public static class AladdinPaintWriter {

  public static byte[] ToBytes(AladdinPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AladdinPaintFile.FileSize];
    var span = result.AsSpan();

    var header = new AladdinPaintHeader((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(AladdinPaintHeader.StructSize));

    return result;
  }
}
