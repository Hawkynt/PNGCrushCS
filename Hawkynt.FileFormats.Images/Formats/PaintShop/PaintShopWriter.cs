using System;

namespace FileFormat.PaintShop;

/// <summary>Assembles PaintShop page bytes.</summary>
public static class PaintShopWriter {

  public static byte[] ToBytes(PaintShopFile file) {
    var result = new byte[PaintShopFile.FileSize];
    var data = file.BitmapData ?? [];
    data.AsSpan(0, Math.Min(data.Length, PaintShopFile.FileSize)).CopyTo(result);

    return result;
  }
}
