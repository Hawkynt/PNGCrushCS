using System;

namespace FileFormat.PrintShop;

/// <summary>Assembles Print Shop PSA format bytes from a <see cref="PrintShopFile"/>.</summary>
public static class PrintShopWriter {

  public static byte[] ToBytes(PrintShopFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PrintShopFile.PsaFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, PrintShopFile.PixelDataSize)).CopyTo(result);
    return result;
  }
}
