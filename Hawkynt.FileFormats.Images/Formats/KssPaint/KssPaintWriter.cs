using System;

namespace FileFormat.KssPaint;

/// <summary>Assembles KSS-Paint (.bkg) file bytes.</summary>
public static class KssPaintWriter {

  public static byte[] ToBytes(KssPaintFile file) {
    var result = new byte[KssPaintFile.FileSize];

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, KssPaintFile.BitmapDataSize)).CopyTo(result);

    var colors = file.Colors ?? [];
    colors.AsSpan(0, Math.Min(colors.Length, KssPaintFile.ColorCount))
      .CopyTo(result.AsSpan(KssPaintFile.ColorOffset));

    return result;
  }
}
