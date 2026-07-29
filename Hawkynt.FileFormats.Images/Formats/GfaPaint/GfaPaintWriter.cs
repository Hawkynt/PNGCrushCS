using System;

namespace FileFormat.GfaPaint;

/// <summary>Assembles GFA Paint file bytes from an in-memory representation.</summary>
public static class GfaPaintWriter {

  public static byte[] ToBytes(GfaPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GfaPaintFile.FileSize];
    var span = result.AsSpan();

    var header = new GfaPaintHeader((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(GfaPaintHeader.StructSize));

    return result;
  }
}
