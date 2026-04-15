using System;

namespace FileFormat.ImagicPaint;

/// <summary>Assembles Imagic Paint file bytes from an in-memory representation.</summary>
public static class ImagicPaintWriter {

  public static byte[] ToBytes(ImagicPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ImagicPaintFile.FileSize];
    var span = result.AsSpan();

    var header = new ImagicPaintHeader((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(ImagicPaintHeader.StructSize));

    return result;
  }
}
