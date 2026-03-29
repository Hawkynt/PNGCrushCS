using System;

namespace FileFormat.PicWorks;

/// <summary>Assembles PicWorks file bytes from an in-memory representation.</summary>
public static class PicWorksWriter {

  public static byte[] ToBytes(PicWorksFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PicWorksFile.FileSize];
    var span = result.AsSpan();

    var header = PicWorksHeader.FromPalette((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(PicWorksHeader.StructSize));

    return result;
  }
}
