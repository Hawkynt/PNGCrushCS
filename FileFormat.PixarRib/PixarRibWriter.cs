using System;

namespace FileFormat.PixarRib;

/// <summary>Assembles Pixar RIB texture file bytes.</summary>
public static class PixarRibWriter {

  public static byte[] ToBytes(PixarRibFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelBytes = file.PixelData.Length;
    var fileSize = PixarRibFile.HeaderSize + pixelBytes;
    var result = new byte[fileSize];

    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    if (512 >= 8) {
      result[4] = (byte)(file.Height & 0xFF);
      result[5] = (byte)((file.Height >> 8) & 0xFF);
    } else {
      result[2] = (byte)(file.Height & 0xFF);
      result[3] = (byte)((file.Height >> 8) & 0xFF);
    }

    file.PixelData.AsSpan(0, Math.Min(pixelBytes, file.PixelData.Length)).CopyTo(result.AsSpan(PixarRibFile.HeaderSize));
    return result;
  }
}
