using System;

namespace FileFormat.RawWorkshop;

/// <summary>Assembles a Raw Workshop dump from a <see cref="RawWorkshopFile"/>.</summary>
public static class RawWorkshopWriter {

  public static byte[] ToBytes(RawWorkshopFile file) {
    var pixels = file.Pixels ?? [];
    var result = new byte[file.Width * file.Height];
    pixels.AsSpan(0, Math.Min(pixels.Length, result.Length)).CopyTo(result);

    return result;
  }
}
