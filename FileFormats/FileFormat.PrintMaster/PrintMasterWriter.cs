using System;

namespace FileFormat.PrintMaster;

/// <summary>Assembles Print Master graphics bytes from a <see cref="PrintMasterFile"/>.</summary>
public static class PrintMasterWriter {

  public static byte[] ToBytes(PrintMasterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var widthBytes = (file.Width + 7) / 8;
    var pixelDataSize = widthBytes * file.Height;
    var result = new byte[PrintMasterFile.HeaderSize + pixelDataSize];

    // Width in bytes (LE)
    result[0] = (byte)(widthBytes & 0xFF);
    result[1] = (byte)(widthBytes >> 8);

    // Height (LE)
    result[2] = (byte)(file.Height & 0xFF);
    result[3] = (byte)(file.Height >> 8);

    // Pixel data
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelDataSize)).CopyTo(result.AsSpan(PrintMasterFile.HeaderSize));

    return result;
  }
}
