using System;

namespace FileFormat.Portrait;

/// <summary>Writes Portrait pictures (.cvp).</summary>
public static class PortraitWriter {

  public static byte[] ToBytes(PortraitFile file) {
    if (file.PlaneData == null || file.PlaneData.Length != PortraitFile.FileSize)
      throw new ArgumentException($"A Portrait picture is exactly {PortraitFile.FileSize} bytes of RGB planes.", nameof(file));

    return file.PlaneData[..];
  }
}
