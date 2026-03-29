using System;

namespace FileFormat.HiresManager;

/// <summary>Assembles Hires Manager by Cosmos (.him) file bytes from a HiresManagerFile.</summary>
public static class HiresManagerWriter {

  public static byte[] ToBytes(HiresManagerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiresManagerFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(HiresManagerFile.LoadAddressSize));

    return result;
  }
}
