using System;

namespace FileFormat.Flimatic;

/// <summary>Assembles Commodore 64 Flimatic (.flm) file bytes from a FlimaticFile.</summary>
public static class FlimaticWriter {

  public static byte[] ToBytes(FlimaticFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FlimaticFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(FlimaticFile.LoadAddressSize));

    return result;
  }
}
