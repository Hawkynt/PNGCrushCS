using System;

namespace FileFormat.SbigCcd;

/// <summary>Assembles SBIG CCD file bytes from a <see cref="SbigCcdFile"/>.</summary>
public static class SbigCcdWriter {

  public static byte[] ToBytes(SbigCcdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SbigCcdFile.HeaderSize + file.PixelData.Length];
    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    result[2] = (byte)(file.Height & 0xFF);
    result[3] = (byte)((file.Height >> 8) & 0xFF);
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(SbigCcdFile.HeaderSize));
    return result;
  }
}
