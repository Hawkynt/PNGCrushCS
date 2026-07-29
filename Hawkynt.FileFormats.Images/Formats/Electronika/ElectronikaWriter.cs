using System;

namespace FileFormat.Electronika;

/// <summary>Assembles Electronika BK screen dump file bytes.</summary>
public static class ElectronikaWriter {

  public static byte[] ToBytes(ElectronikaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[ElectronikaFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, ElectronikaFile.FileSize)).CopyTo(result);
    return result;
  }
}
