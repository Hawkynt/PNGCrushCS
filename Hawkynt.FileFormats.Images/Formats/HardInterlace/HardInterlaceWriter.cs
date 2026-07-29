using System;

namespace FileFormat.HardInterlace;

/// <summary>Assembles Hard Interlace (.hip) file bytes from a HardInterlaceFile.</summary>
public static class HardInterlaceWriter {

  public static byte[] ToBytes(HardInterlaceFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HardInterlaceFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(HardInterlaceFile.LoadAddressSize));

    return result;
  }
}
