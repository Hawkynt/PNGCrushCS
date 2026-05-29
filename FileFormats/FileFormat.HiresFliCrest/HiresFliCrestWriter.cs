using System;

namespace FileFormat.HiresFliCrest;

/// <summary>Assembles Hires FLI by Crest (.hfc) file bytes from a HiresFliCrestFile.</summary>
public static class HiresFliCrestWriter {

  public static byte[] ToBytes(HiresFliCrestFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiresFliCrestFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(HiresFliCrestFile.LoadAddressSize));

    return result;
  }
}
