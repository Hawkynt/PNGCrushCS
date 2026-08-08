using System;

namespace FileFormat.AtariHighResPage;

/// <summary>Assembles the picture behind whatever header it came with.</summary>
public static class AtariHighResPageWriter {

  public static byte[] ToBytes(AtariHighResPageFile file) {
    var header = file.Header ?? [];
    if (header.Length is 0 or > AtariHighResPageFile.MaxHeaderSize)
      header = new byte[AtariHighResPageFile.SampleHeaderSize];

    var pixels = file.PixelData ?? [];
    var result = new byte[header.Length + AtariHighResPageFile.BitmapSize];
    header.CopyTo(result.AsSpan());
    result[0] = 0;
    result[1] = AtariHighResPageFile.HighResolution;
    pixels.AsSpan(0, Math.Min(pixels.Length, AtariHighResPageFile.BitmapSize))
      .CopyTo(result.AsSpan(header.Length));

    return result;
  }
}
