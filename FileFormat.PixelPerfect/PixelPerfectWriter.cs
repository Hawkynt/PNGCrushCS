using System;

namespace FileFormat.PixelPerfect;

/// <summary>Assembles Pixel Perfect (.pp/.ppp) file bytes from a PixelPerfectFile.</summary>
public static class PixelPerfectWriter {

  public static byte[] ToBytes(PixelPerfectFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PixelPerfectFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(PixelPerfectFile.LoadAddressSize));

    return result;
  }
}
