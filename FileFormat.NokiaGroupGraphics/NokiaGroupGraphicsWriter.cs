using System;

namespace FileFormat.NokiaGroupGraphics;

/// <summary>Assembles Nokia Group Graphics NGG file bytes from a NokiaGroupGraphicsFile.</summary>
public static class NokiaGroupGraphicsWriter {

  public static byte[] ToBytes(NokiaGroupGraphicsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[NokiaGroupGraphicsFile.HeaderSize + pixelDataSize];

    result[0] = NokiaGroupGraphicsFile.Magic[0];
    result[1] = NokiaGroupGraphicsFile.Magic[1];
    result[2] = NokiaGroupGraphicsFile.Magic[2];
    result[3] = file.Version;
    result[4] = (byte)file.Width;
    result[5] = (byte)file.Height;

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(NokiaGroupGraphicsFile.HeaderSize));

    return result;
  }
}
