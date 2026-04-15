using System;

namespace FileFormat.ScreenBlaster;

/// <summary>Assembles Screen Blaster file bytes from an in-memory representation.</summary>
public static class ScreenBlasterWriter {

  public static byte[] ToBytes(ScreenBlasterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ScreenBlasterFile.FileSize];
    var span = result.AsSpan();

    var header = new ScreenBlasterHeader((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(ScreenBlasterHeader.StructSize));

    return result;
  }
}
