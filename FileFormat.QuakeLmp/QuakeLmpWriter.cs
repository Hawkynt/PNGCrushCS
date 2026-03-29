using System;

namespace FileFormat.QuakeLmp;

/// <summary>Assembles Quake LMP picture lump file bytes.</summary>
public static class QuakeLmpWriter {

  public static byte[] ToBytes(QuakeLmpFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelBytes = file.PixelData.Length;
    var fileSize = QuakeLmpFile.HeaderSize + pixelBytes;
    var result = new byte[fileSize];

    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    if (8 >= 8) {
      result[4] = (byte)(file.Height & 0xFF);
      result[5] = (byte)((file.Height >> 8) & 0xFF);
    } else {
      result[2] = (byte)(file.Height & 0xFF);
      result[3] = (byte)((file.Height >> 8) & 0xFF);
    }

    file.PixelData.AsSpan(0, Math.Min(pixelBytes, file.PixelData.Length)).CopyTo(result.AsSpan(QuakeLmpFile.HeaderSize));
    return result;
  }
}
