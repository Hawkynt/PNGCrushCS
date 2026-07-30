using System;

namespace FileFormat.PrintShopIcon;

/// <summary>Assembles Print Shop graphic bytes.</summary>
public static class PrintShopIconWriter {

  public static byte[] ToBytes(PrintShopIconFile file) {
    var result = new byte[PrintShopIconFile.BitmapSize];
    var data = file.BitmapData ?? [];
    data.AsSpan(0, Math.Min(data.Length, PrintShopIconFile.BitmapSize)).CopyTo(result);

    return result;
  }
}
