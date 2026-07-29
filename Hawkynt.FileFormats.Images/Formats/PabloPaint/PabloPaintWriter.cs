using System;

namespace FileFormat.PabloPaint;

/// <summary>Assembles Atari ST Pablo Paint file bytes from a <see cref="PabloPaintFile"/>.</summary>
public static class PabloPaintWriter {

  public static byte[] ToBytes(PabloPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PabloPaintFile.FileSize];

    PabloPaintFile.Banner.CopyTo(result);
    result[PabloPaintFile.ResolutionOffset] = PabloPaintFile.HighResolutionMode;

    // Three fixed bytes sit between the resolution and the palette; readers check them.
    result[44] = 0;
    result[45] = 125;
    result[46] = 36;

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, PabloPaintFile.PixelDataSize))
      .CopyTo(result.AsSpan(PabloPaintFile.PixelDataOffset));

    return result;
  }
}
