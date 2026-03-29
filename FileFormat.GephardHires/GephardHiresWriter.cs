using System;

namespace FileFormat.GephardHires;

/// <summary>Assembles Gephard Hires (.ghg) file bytes from a GephardHiresFile.</summary>
public static class GephardHiresWriter {

  public static byte[] ToBytes(GephardHiresFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GephardHiresFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(GephardHiresFile.LoadAddressSize));

    return result;
  }
}
