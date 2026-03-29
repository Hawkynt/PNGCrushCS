using System;

namespace FileFormat.GoDot4Bit;

/// <summary>Assembles Commodore 64 GoDot 4-bit file bytes from a GoDot4BitFile.</summary>
public static class GoDot4BitWriter {

  public static byte[] ToBytes(GoDot4BitFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GoDot4BitFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, GoDot4BitFile.ExpectedFileSize)).CopyTo(result.AsSpan(0));

    return result;
  }
}
