using System;

namespace FileFormat.Gigacad;

/// <summary>Assembles Atari ST GigaCAD monochrome image bytes from a GigacadFile.</summary>
public static class GigacadWriter {

  public static byte[] ToBytes(GigacadFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GigacadFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, GigacadFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
