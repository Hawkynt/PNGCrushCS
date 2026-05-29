using System;

namespace FileFormat.RamBrandt;

/// <summary>Assembles Ram Brandt file bytes from a <see cref="RamBrandtFile"/>.</summary>
public static class RamBrandtWriter {

  public static byte[] ToBytes(RamBrandtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[RamBrandtFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, RamBrandtFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
