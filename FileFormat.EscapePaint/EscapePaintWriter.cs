using System;

namespace FileFormat.EscapePaint;

/// <summary>Assembles Escape Paint file bytes from an in-memory representation.</summary>
public static class EscapePaintWriter {

  public static byte[] ToBytes(EscapePaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[EscapePaintFile.FileSize];
    var span = result.AsSpan();

    var header = EscapePaintHeader.FromPalette((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(EscapePaintHeader.StructSize));

    return result;
  }
}
