using System;

namespace FileFormat.CutCreator;

/// <summary>Assembles a Cut Creator picture, which is its bitmap and nothing else.</summary>
public static class CutCreatorWriter {

  public static byte[] ToBytes(CutCreatorFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[CutCreatorFile.FileSize];
    pixels.AsSpan(0, Math.Min(pixels.Length, result.Length)).CopyTo(result);

    return result;
  }
}
