using System;

namespace FileFormat.NokiaLogo;

/// <summary>Assembles Nokia Operator Logo file bytes.</summary>
public static class NokiaLogoWriter {

  public static byte[] ToBytes(NokiaLogoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[NokiaLogoFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, NokiaLogoFile.FileSize)).CopyTo(result);
    return result;
  }
}
